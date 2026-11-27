using System.Globalization;
using System.Text;
using DotNet.Testcontainers.Containers;

namespace Haruspex.DifferentialTests.Server;

/// <summary>
/// Starts the test server's container, and tries once more when the container was made but stopped or never became ready,
/// writing down each failed attempt's state, exit code and log. On CI the first attempt of a run has twice ended with every
/// server test failing on "container … is not running": Testcontainers' readiness check runs <c>sqlcmd</c> in the container,
/// and Docker refuses that once SQL Server has exited. A rerun passed both times, and nothing said why SQL Server had
/// stopped, so the log is kept to explain the next one.
/// </summary>
/// <remarks>
/// <para>
/// <b>One retry, not more.</b> A container that fails at random once in many runs is very unlikely to fail twice running, so a
/// second attempt turns almost every such failure into a pass. A container that fails every time (a bad image, too little
/// memory) fails the same way on the second attempt, and more attempts would only make the run slower before it says so.
/// Two failed logs side by side also show whether the failure repeats.
/// </para>
/// <para>
/// <b>Only a failed start is retried</b>: an exception from <see cref="IContainer.StartAsync"/> after Docker had made the
/// container. No container (Docker not running, the image not pulled) is not a start that went wrong, and is reported at once;
/// and nothing after the start (deploying the fixtures, a test) is ever tried again here.
/// </para>
/// </remarks>
internal static class ContainerStart
{
    /// <summary>How many times a container is made and started, at most.</summary>
    internal const int Attempts = 2;

    /// <summary>
    /// How long one attempt may take, including pulling the image (about 1.5 GB) when it isn't there yet. SQL Server's own
    /// start takes under a minute on CI's runners, so a container still not ready after this long won't become ready.
    /// </summary>
    internal static readonly TimeSpan Patience = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The most lines kept from each end of each of the container's streams, so a long log can't flood the output. Both ends,
    /// because each can hold the reason: when SQL Server crashes, its standard error begins with the fatal error and its stack,
    /// then fills with the crash handler's own complaints (observed with the pinned image: about 200 lines of them).
    /// </summary>
    private const int LogLines = 100;

    /// <summary>
    /// Makes a container with <paramref name="build"/> (given the attempt's number, from 1) and starts it, making and starting
    /// another once if that failed. A failed attempt's container is disposed, so Docker removes it, before the next is made.
    /// </summary>
    /// <param name="report">
    /// Given the account of the failed first attempt when the second succeeds, so a start that needed a retry still says why.
    /// </param>
    /// <exception cref="ContainerStartException">Every attempt made a container that didn't start; it holds each one's account.</exception>
    internal static async Task<TContainer> StartAsync<TContainer>(Func<int, TContainer> build, TimeSpan patience, Action<string> report)
        where TContainer : IContainer
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(report);
        var accounts = new List<string>();
        for (var attempt = 1; ; attempt++)
        {
            var container = build(attempt);
            try
            {
                using var timeout = new CancellationTokenSource(patience);
                await container.StartAsync(timeout.Token).ConfigureAwait(false);
                if (accounts.Count > 0)
                    report($"SQL Server's container started at attempt {attempt} of {Attempts}, after this:{Environment.NewLine}{string.Join(Environment.NewLine, accounts)}");
                return container;
            }
#pragma warning disable CA1031 // Whatever stopped the start, the account says what it was; the last attempt's exception is kept as the inner one.
            catch (Exception e) when (container.State != TestcontainersStates.Undefined)
#pragma warning restore CA1031
            {
                accounts.Add(await DescribeAsync(container, attempt, e).ConfigureAwait(false));
                await container.DisposeAsync().ConfigureAwait(false);
                if (attempt == Attempts)
                    throw new ContainerStartException(string.Join(Environment.NewLine, accounts), e);
            }
            catch
            {
                await container.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
    }

    /// <summary>
    /// What a failed attempt left to see: the exception, whether the container had exited and with what code, and the end of
    /// each of its streams. Reading any of them can fail too (Docker itself may be what went wrong), and then says so.
    /// </summary>
    private static async Task<string> DescribeAsync(IContainer container, int attempt, Exception failure)
    {
        var text = new StringBuilder();
        // The message's first line only: Testcontainers puts the container's log in its own exception when it sees the
        // container exit (ContainerNotRunningException), though not when Docker refuses the readiness check first (a
        // DockerApiException, "container … is not running", as on CI), so the log is read below either way.
        var message = failure.Message.Split('\n', 2)[0].TrimEnd('\r');
        text.AppendLine(CultureInfo.InvariantCulture, $"Attempt {attempt} of {Attempts}: container {container.Name.TrimStart('/')} did not start ({failure.GetType().Name}: {message})");

        // Docker's wait for the exit code returns at once for a container that has exited, and waits for one still running,
        // so a short wait tells the two apart.
        using (var wait = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            try
            {
                var code = await container.GetExitCodeAsync(wait.Token).ConfigureAwait(false);
                text.AppendLine(CultureInfo.InvariantCulture, $"It had exited, with code {code}.");
            }
#pragma warning disable CA1031 // The wait's own timeout, whatever exception carries it.
            catch (Exception) when (wait.IsCancellationRequested)
#pragma warning restore CA1031
            {
                text.AppendLine("It was still running.");
            }
#pragma warning disable CA1031 // Reported, not thrown: the account is still worth having without the exit code.
            catch (Exception e)
#pragma warning restore CA1031
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"Its exit code could not be read ({e.GetType().Name}: {e.Message}).");
            }
        }

        using (var wait = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
        {
            try
            {
                var (stdout, stderr) = await container.GetLogsAsync(ct: wait.Token).ConfigureAwait(false);
                Append(text, "standard output", stdout);
                Append(text, "standard error", stderr);
            }
#pragma warning disable CA1031 // As above.
            catch (Exception e)
#pragma warning restore CA1031
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"Its log could not be read ({e.GetType().Name}: {e.Message}).");
            }
        }

        return text.ToString();
    }

    private static void Append(StringBuilder text, string stream, string log)
    {
        var lines = log.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToList();
        if (lines.Count == 0)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"Its {stream} was empty.");
            return;
        }

        text.AppendLine(CultureInfo.InvariantCulture, $"Its {stream}:");
        if (lines.Count <= 2 * LogLines)
        {
            foreach (var line in lines)
                text.Append("    ").AppendLine(line);
            return;
        }

        foreach (var line in lines.Take(LogLines))
            text.Append("    ").AppendLine(line);
        text.AppendLine(CultureInfo.InvariantCulture, $"    ({lines.Count - (2 * LogLines)} lines left out)");
        foreach (var line in lines.TakeLast(LogLines))
            text.Append("    ").AppendLine(line);
    }
}
