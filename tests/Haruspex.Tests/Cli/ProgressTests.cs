using Haruspex.Cli;
using Haruspex.Cli.Output;

namespace Haruspex.Tests.Cli;

/// <summary>
/// The progress line: drawn only once a run has taken long enough to need it, redrawn in place as it moves on, cleared when it
/// is done, and never drawn where standard error isn't a terminal or the run is in CI.
/// </summary>
public class ProgressTests
{
    private static readonly OutputSettings OnATerminal = new(OutputDetail.Normal, TextStyle.Plain, TextStyle.Plain, progress: true);

    [Fact]
    public void A_run_quicker_than_the_delay_draws_nothing()
    {
        var clock = new ManualClock();
        using var error = new StringWriter();

        using (var progress = Progress.On(error, OnATerminal, new Terminal { ErrorIsTerminal = true, Clock = clock }))
        {
            progress.Step("reading db");
            clock.Advance(Progress.Delay - TimeSpan.FromMilliseconds(1));
        }

        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void A_longer_run_draws_its_step_redraws_it_in_place_and_clears_it_at_the_end()
    {
        var clock = new ManualClock();
        using var error = new StringWriter();

        using (var progress = Progress.On(error, OnATerminal, new Terminal { ErrorIsTerminal = true, Clock = clock }))
        {
            progress.Step("reading db");
            clock.Advance(Progress.Delay);
            progress.Step("inferring 12 procedures");
            clock.Advance(TimeSpan.FromSeconds(2));
        }

        Assert.Equal(
            "\rharuspex: reading db (0s)" +
            "\rharuspex: inferring 12 procedures (0s)" +
            "\rharuspex: inferring 12 procedures (1s)" +
            "\rharuspex: inferring 12 procedures (2s)" +
            "\r" + new string(' ', "haruspex: inferring 12 procedures (2s)".Length) + "\r",
            error.ToString());
    }

    [Fact]
    public void A_line_wider_than_the_terminal_is_cut_short()
    {
        var clock = new ManualClock();
        using var error = new StringWriter();

        using (var progress = Progress.On(error, OnATerminal, new Terminal { ErrorIsTerminal = true, Clock = clock, Width = 21 }))
        {
            progress.Step("reading a/very/long/path/to/the/database");
            clock.Advance(Progress.Delay);
        }

        Assert.StartsWith("\rharuspex: reading a…\r", error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, "true")]
    public void A_run_draws_no_progress_off_a_terminal_or_in_ci(bool errorIsTerminal, string? ci)
    {
        var clock = new ManualClock();
        using var error = new StringWriter();
        var terminal = new Terminal
        {
            ErrorIsTerminal = errorIsTerminal,
            Clock = clock,
            Environment = ci is null ? [] : new Dictionary<string, string> { ["CI"] = ci },
        };

        using (var progress = Progress.On(error, OutputSettings.Settle(OutputDetail.Normal, ColourChoice.Auto, terminal), terminal))
        {
            progress.Step("reading db");
            clock.Advance(TimeSpan.FromSeconds(5));
        }

        Assert.Equal("", error.ToString());
    }
}
