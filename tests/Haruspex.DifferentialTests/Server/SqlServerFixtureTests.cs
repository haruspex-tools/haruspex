using System.Diagnostics;
using System.Text.RegularExpressions;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Testcontainers.MsSql;

namespace Haruspex.DifferentialTests.Server;

/// <summary>
/// What the fixture does before any test needs the server, checked without one; and how it starts its container
/// (<see cref="ContainerStart"/>), checked with containers of the pinned image made to fail on purpose. Those need Docker, so
/// they skip without it, as the differential tests do, unless <c>HARUSPEX_REQUIRE_SQLSERVER=1</c>.
/// </summary>
public class SqlServerFixtureTests
{
    // Every container these tests make is named with this test's own prefix, so it can tell whether any is left behind.
    private readonly string prefix = $"hx-start-{Guid.NewGuid().ToString("N")[..8]}-";

    private int builds;

    /// <summary>
    /// Every test in the assembly shares the fixture, so a run of tests that need no server (the validator's, the pool's) must
    /// not wait for one: nothing is started, created or deployed until a test asks (<see cref="SqlServerFixture.RequireAsync"/>).
    /// </summary>
    [Fact]
    public async Task The_server_is_not_set_up_until_a_test_needs_it()
    {
        await using var fixture = new SqlServerFixture();

        await fixture.InitializeAsync();

        Assert.False(fixture.Started);
        Assert.Equal(0, fixture.Rooms);
    }

    /// <summary>
    /// A server given to the fixture is the one it sets up, and no container is built: here one nothing listens for, so the setup
    /// fails on it, connecting. Needs no Docker.
    /// </summary>
    [Fact]
    public async Task A_server_given_to_the_fixture_is_used_and_no_container_is_built()
    {
        await using var fixture = new SqlServerFixture(Unreachable, NoContainer, ContainerStart.Patience, _ => throw new InvalidOperationException("No container, so nothing to connect to."), required: true, Ignore);

        var e = await Record.ExceptionAsync(fixture.RequireAsync);

        Assert.Equal(0, builds);
        Assert.Contains("Setting up the test database failed", e!.Message, StringComparison.Ordinal);
        Assert.Contains("SqlException", e.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// SQL Server exits during its start, as on CI, when its administrator's password is too short, and says why. Both
    /// attempts' accounts say that it had exited, with its code and its log, and neither container is left behind.
    /// </summary>
    [Fact]
    public async Task A_container_that_exits_during_its_start_is_started_once_more_and_each_attempt_reports_its_log()
    {
        RequireDocker();

        var e = await FailToStartAsync(Exiting, ContainerStart.Patience);

        Assert.Equal(2, builds);
        Assert.Contains("Attempt 1 of 2", e.Message, StringComparison.Ordinal);
        Assert.Contains("Attempt 2 of 2", e.Message, StringComparison.Ordinal);
        Assert.Equal(2, Count(e.Message, "It had exited, with code "));
        Assert.Equal(2, Logs(e.Message));
        Assert.Empty(await LeftAsync());
    }

    /// <summary>A start that succeeds at the second attempt still reports why the first failed, with its log.</summary>
    [Fact]
    public async Task A_container_that_starts_at_the_second_attempt_reports_the_first()
    {
        RequireDocker();
        var reports = new List<string>();

        var container = await ContainerStart.StartAsync(
            attempt => attempt == 1 ? Exiting(attempt) : Sleeping(attempt), ContainerStart.Patience, reports.Add);
        await container.DisposeAsync();

        Assert.Equal(2, builds);
        var report = Assert.Single(reports);
        Assert.Contains("started at attempt 2 of 2", report, StringComparison.Ordinal);
        Assert.Contains("Attempt 1 of 2", report, StringComparison.Ordinal);
        Assert.Equal(1, Logs(report));
        Assert.Empty(await LeftAsync());
    }

    /// <summary>A container still running but not ready when its time is up is a failed start too, and is removed.</summary>
    [Fact]
    public async Task A_container_that_never_becomes_ready_is_given_up_after_one_retry()
    {
        RequireDocker();

        var e = await FailToStartAsync(NeverReady, TimeSpan.FromSeconds(3));

        Assert.Equal(2, builds);
        Assert.Equal(2, Count(e.Message, "It was still running."));
        Assert.Empty(await LeftAsync());
    }

    /// <summary>
    /// Through the fixture: the first test to need a server whose container didn't start is told why with the log, and every
    /// later test is told where to find it. No test starts the container again.
    /// </summary>
    [Fact]
    public async Task The_first_test_to_need_the_server_is_given_the_log_of_a_start_that_failed()
    {
        RequireDocker();
        await using var fixture = new SqlServerFixture(given: null, Exiting, ContainerStart.Patience, _ => throw new InvalidOperationException("Never started."), required: true, Ignore);

        var first = await Record.ExceptionAsync(fixture.RequireAsync);
        var second = await Record.ExceptionAsync(fixture.RequireAsync);

        Assert.Equal(2, builds);
        Assert.Contains("did not start at any of 2 attempts", first!.Message, StringComparison.Ordinal);
        Assert.Equal(2, Logs(first.Message));
        Assert.Contains("did not start at any of 2 attempts", second!.Message, StringComparison.Ordinal);
        Assert.Contains("printed with the first test to report this", second.Message, StringComparison.Ordinal);
        Assert.Equal(0, Logs(second.Message));
    }

    /// <summary>
    /// A container that started is not started again, whatever fails after it: here the server in it can't be reached, so
    /// setting up the test database fails, and every test that needs the server is told so.
    /// </summary>
    [Fact]
    public async Task A_failure_after_the_container_started_does_not_start_it_again()
    {
        RequireDocker();
        await using var fixture = new SqlServerFixture(given: null, Sleeping, ContainerStart.Patience, _ => Task.FromResult(Unreachable), required: true, Ignore);

        var first = await Record.ExceptionAsync(fixture.RequireAsync);
        var second = await Record.ExceptionAsync(fixture.RequireAsync);

        Assert.Equal(1, builds);
        Assert.Contains("Setting up the test database failed", first!.Message, StringComparison.Ordinal);
        Assert.Contains("Setting up the test database failed", second!.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A container that started and then stopped while the test database was set up isn't started again, but its log reaches
    /// the first test to need the server, and later tests are told where it is.
    /// </summary>
    [Fact]
    public async Task A_container_that_stops_during_the_setup_is_described_to_the_first_test()
    {
        RequireDocker();
        var reports = new List<string>();
        await using var fixture = new SqlServerFixture(
            given: null,
            Stopping,
            ContainerStart.Patience,
            async c =>
            {
                await c.GetExitCodeAsync(TestContext.Current.CancellationToken);
                return Unreachable;
            },
            required: true,
            reports.Add);

        var first = await Record.ExceptionAsync(fixture.RequireAsync);
        var second = await Record.ExceptionAsync(fixture.RequireAsync);

        Assert.Equal(1, builds);
        Assert.Contains("Setting up the test database failed", first!.Message, StringComparison.Ordinal);
        Assert.Contains("had stopped while the test database was set up", first.Message, StringComparison.Ordinal);
        Assert.Contains("It had exited, with code 7.", first.Message, StringComparison.Ordinal);
        Assert.Contains(StoppingLine, first.Message, StringComparison.Ordinal);
        Assert.Contains("printed with the first test to report this", second!.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(StoppingLine, second.Message, StringComparison.Ordinal);
        Assert.Single(reports);
    }

    /// <summary>
    /// A container that stops after the setup, during the tests, fails them with connection errors that don't say why; when the
    /// fixture is disposed its log is reported.
    /// </summary>
    [Fact]
    public async Task A_container_that_stops_during_the_tests_is_described_when_the_fixture_is_disposed()
    {
        RequireDocker();
        var reports = new List<string>();
        IContainer? made = null;
        var fixture = new SqlServerFixture(given: null, a => made = StoppingWhenTold(a), ContainerStart.Patience, _ => Task.FromResult(Unreachable), required: true, reports.Add);
        try
        {
            await Record.ExceptionAsync(fixture.RequireAsync);
            Assert.Empty(reports);
            var told = await made!.ExecAsync(["touch", StopFile], TestContext.Current.CancellationToken);
            Assert.Equal(0, told.ExitCode);
            await made.GetExitCodeAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            await fixture.DisposeAsync();
        }

        var report = Assert.Single(reports);
        Assert.Contains("had stopped by the end of the run", report, StringComparison.Ordinal);
        Assert.Contains("It had exited, with code 7.", report, StringComparison.Ordinal);
        Assert.Contains(StoppingLine, report, StringComparison.Ordinal);
    }

    /// <summary>The fixture's container is removed when the fixture is disposed, not left for Docker to clean up after the run.</summary>
    [Fact]
    public async Task Disposing_the_fixture_removes_its_container()
    {
        RequireDocker();
        var reports = new List<string>();
        var fixture = new SqlServerFixture(given: null, Sleeping, ContainerStart.Patience, _ => Task.FromResult(Unreachable), required: true, reports.Add);
        IReadOnlyList<string> before;
        try
        {
            await Record.ExceptionAsync(fixture.RequireAsync);
            before = await LeftAsync();
        }
        finally
        {
            await fixture.DisposeAsync();
        }

        Assert.Single(before);
        Assert.Empty(await LeftAsync());
        Assert.Empty(reports);
    }

    // A server nothing listens for (port 1), given up on quickly.
    internal const string Unreachable = "Server=tcp:127.0.0.1,1;User Id=sa;Password=unused;Connect Timeout=2;TrustServerCertificate=True";

    /// <summary>
    /// SQL Server given an administrator's password too short for its policy: it says so and exits with code 255, about ten
    /// seconds after it starts, and Testcontainers' readiness check fails as CI's did, on Docker's "container … is not running".
    /// </summary>
    private MsSqlContainer Exiting(int attempt) =>
        Count(new MsSqlBuilder(SqlServerFixture.Image).WithName($"{prefix}exits-{attempt}").WithPassword("short").Build());

    /// <summary>A container of the same image that starts at once and does nothing, since it waits for nothing.</summary>
    private IContainer Sleeping(int attempt) =>
        Count(new ContainerBuilder(SqlServerFixture.Image).WithName($"{prefix}sleeps-{attempt}").WithEntrypoint("sleep").WithCommand("600").Build());

    // What the stopping container prints before it exits.
    private const string StoppingLine = "the server in this container has stopped";

    /// <summary>A container that starts at once, since it waits for nothing, says it has stopped two seconds later and exits with code 7.</summary>
    private IContainer Stopping(int attempt) =>
        Count(new ContainerBuilder(SqlServerFixture.Image).WithName($"{prefix}stops-{attempt}").WithEntrypoint("sh").WithCommand("-c", $"sleep 2; echo {StoppingLine}; exit 7").Build());

    // The file a test makes in a StoppingWhenTold container to stop it.
    private const string StopFile = "/tmp/stop";

    /// <summary>
    /// A container that starts at once and runs until the test makes <see cref="StopFile"/> in it, then says it has stopped and
    /// exits with code 7: so it is running for as long as the test needs, however slow the machine, and stopped after.
    /// </summary>
    private IContainer StoppingWhenTold(int attempt) =>
        Count(new ContainerBuilder(SqlServerFixture.Image).WithName($"{prefix}stops-when-told-{attempt}").WithEntrypoint("sh")
            .WithCommand("-c", $"while [ ! -e {StopFile} ]; do sleep 0.2; done; echo {StoppingLine}; exit 7").Build());

    /// <summary>A container that runs but waits for a file nothing makes, so it is never ready.</summary>
    private IContainer NeverReady(int attempt) =>
        Count(new ContainerBuilder(SqlServerFixture.Image).WithName($"{prefix}waits-{attempt}").WithEntrypoint("sleep").WithCommand("600")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilFileExists("/never", FileSystem.Container)).Build());

    /// <summary>Counted as built, but throws rather than make a container, so a test that should build none needs no Docker.</summary>
    private IContainer NoContainer(int attempt)
    {
        builds++;
        throw new InvalidOperationException($"Attempt {attempt} built a container, but this test gives the fixture none to build.");
    }

    /// <summary>The exception from a start expected to fail; a container that started after all is disposed, not left running.</summary>
    private static async Task<ContainerStartException> FailToStartAsync(Func<int, IContainer> build, TimeSpan patience)
    {
        ContainerStartException? failure = null;
        try
        {
            var container = await ContainerStart.StartAsync(build, patience, _ => Assert.Fail("Nothing started, so there is no retry to report."));
            await container.DisposeAsync();
        }
        catch (ContainerStartException e)
        {
            failure = e;
        }

        Assert.NotNull(failure);
        return failure;
    }

    internal static void Ignore(string report)
    {
    }

    private T Count<T>(T container)
    {
        builds++;
        return container;
    }

    /// <summary>
    /// How many of the container's logs <paramref name="text"/> shows: a stream's heading followed by a line of the stream. Not
    /// a line SQL Server prints in particular, since how it stops varies: given the short password it usually says so and exits
    /// with code 255, but under load it has also crashed first (exit code 1, a fatal error in its platform layer).
    /// </summary>
    private static int Logs(string text) => Regex.Count(text, @"^Its standard output:\r?\n    \S", RegexOptions.Multiline);

    private static int Count(string text, string part)
    {
        var count = 0;
        for (var at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    /// <summary>The containers of this test's that Docker still has, running or not, asked of the <c>docker</c> command.</summary>
    private async Task<IReadOnlyList<string>> LeftAsync()
    {
        var start = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "ps", "--all", "--filter", $"name={prefix}", "--format", "{{.Names}}" })
            start.ArgumentList.Add(argument);
        using var docker = Process.Start(start)!;
        var output = await docker.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        await docker.WaitForExitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, docker.ExitCode);
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>Skips the test when there is no Docker, or fails it when <c>HARUSPEX_REQUIRE_SQLSERVER=1</c>, as the differential tests do.</summary>
    private static void RequireDocker()
    {
        try
        {
            _ = new ContainerBuilder(SqlServerFixture.Image).Build();
        }
#pragma warning disable CA1031 // As the fixture: whatever stops Testcontainers (no Docker; a Docker host over SSH, which throws from a type initialiser) is a reason to skip.
        catch (Exception e)
#pragma warning restore CA1031
        {
            if (Environment.GetEnvironmentVariable(SqlServerFixture.RequireVariable) == "1")
                Assert.Fail($"{SqlServerFixture.RequireVariable}=1, but Docker isn't available: {e.Message}");
            Assert.Skip($"Docker isn't available, so the container's start can't be tried: {e.Message}");
        }
    }
}
