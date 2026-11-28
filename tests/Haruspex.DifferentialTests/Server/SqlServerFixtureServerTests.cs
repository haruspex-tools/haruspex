using DotNet.Testcontainers.Containers;

namespace Haruspex.DifferentialTests.Server;

/// <summary>
/// Where the fixture's server comes from: the public constructor, which the differential tests share, reads
/// <see cref="SqlServerFixture.ServerVariable"/>; a fixture made for a test of its own start reads nothing, so it builds its
/// own container even on a developer's machine where the variable is set. Each test sets the variable in the process, so the
/// class runs alone (<see cref="EnvironmentChanging"/>); nothing else reads it once the shared fixture is made. None needs
/// Docker or a server.
/// </summary>
[Collection(typeof(EnvironmentChanging))]
public class SqlServerFixtureServerTests
{
    private int builds;

    [Fact]
    public async Task The_shared_fixture_takes_its_server_from_the_variable()
    {
        await using var fixture = WithVariable(SqlServerFixtureTests.Unreachable, () => new SqlServerFixture());

        Assert.Equal(SqlServerFixtureTests.Unreachable, fixture.Given);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task The_shared_fixture_starts_a_container_when_the_variable_names_no_server(string? value)
    {
        await using var fixture = WithVariable(value, () => new SqlServerFixture());

        Assert.Null(fixture.Given);
    }

    /// <summary>
    /// With the variable set and no server given, the fixture builds a container rather than use the variable's server: here
    /// the build throws, so the test needs no Docker, and the failure says Testcontainers could not start SQL Server.
    /// </summary>
    [Fact]
    public async Task A_fixture_given_no_server_builds_a_container_even_with_the_variable_set()
    {
        var e = await WithVariableAsync(SqlServerFixtureTests.Unreachable, async () =>
        {
            await using var fixture = new SqlServerFixture(given: null, NoContainer, ContainerStart.Patience, _ => throw new InvalidOperationException("No container, so nothing to connect to."), required: true, SqlServerFixtureTests.Ignore);
            return await Record.ExceptionAsync(fixture.RequireAsync);
        });

        Assert.Equal(1, builds);
        Assert.Contains("could not start SQL Server", e!.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Setting up the test database failed", e.Message, StringComparison.Ordinal);
    }

    private IContainer NoContainer(int attempt)
    {
        builds++;
        throw new InvalidOperationException($"Attempt {attempt} built a container; this test has none to build.");
    }

    private static T WithVariable<T>(string? value, Func<T> make)
    {
        var before = Environment.GetEnvironmentVariable(SqlServerFixture.ServerVariable);
        Environment.SetEnvironmentVariable(SqlServerFixture.ServerVariable, value);
        try
        {
            return make();
        }
        finally
        {
            Environment.SetEnvironmentVariable(SqlServerFixture.ServerVariable, before);
        }
    }

    private static async Task<T> WithVariableAsync<T>(string? value, Func<Task<T>> run)
    {
        var before = Environment.GetEnvironmentVariable(SqlServerFixture.ServerVariable);
        Environment.SetEnvironmentVariable(SqlServerFixture.ServerVariable, value);
        try
        {
            return await run();
        }
        finally
        {
            Environment.SetEnvironmentVariable(SqlServerFixture.ServerVariable, before);
        }
    }
}
