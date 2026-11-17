using System.Globalization;

namespace Haruspex.Cli.Output;

/// <summary>
/// A line on standard error saying what a run is doing (<c>haruspex: inferring 120 procedures (3s)</c>), drawn only once the run
/// has taken long enough to need it, redrawn in place as the run moves on, and cleared before the report is written, so it never
/// reaches a log or a pipe. It is drawn with a carriage return and cleared with spaces, so it needs no escape sequence, and only
/// where <see cref="OutputSettings.Progress"/> allows. The clock is the terminal's, so a test moves time on by hand.
/// </summary>
internal sealed class Progress : IDisposable
{
    /// <summary>How long a run goes before the line is drawn: a run quicker than this, which most are, never shows one.</summary>
    public static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(500);

    /// <summary>How often the line is redrawn while a step goes on, so its seconds count up.</summary>
    public static readonly TimeSpan Period = TimeSpan.FromSeconds(1);

#pragma warning disable CA2213 // Standard error is the command's, lent for the run: the progress draws on it and doesn't own it.
    private readonly TextWriter? error;
#pragma warning restore CA2213
    private readonly Terminal terminal;
    private readonly long started;
    private readonly ITimer? timer;
    private readonly Lock gate = new();
    private string step = "";
    private int drawn;
    private bool shown;

    private Progress(TextWriter? error, Terminal terminal)
    {
        this.error = error;
        this.terminal = terminal;
        started = terminal.Clock.GetTimestamp();
        if (error is not null)
            timer = terminal.Clock.CreateTimer(_ => Tick(), null, Delay, Period);
    }

    /// <summary>A progress that shows nothing: for a run whose standard error isn't a terminal, or that is in CI.</summary>
    public static Progress None { get; } = new(null, Terminal.Plain);

    /// <summary>A progress line on <paramref name="error"/> when <paramref name="settings"/> allow one; <see cref="None"/> otherwise.</summary>
    public static Progress On(TextWriter error, OutputSettings settings, Terminal terminal) =>
        settings.Progress ? new Progress(error, terminal) : None;

    /// <summary>Says what the run is doing now: <c>reading db</c>, <c>inferring 12 procedures</c>.</summary>
    public void Step(string what)
    {
        lock (gate)
        {
            step = what;
            if (shown)
                Draw();
        }
    }

    /// <summary>Clears the line, if it was drawn, and stops the clock.</summary>
    public void Dispose()
    {
        timer?.Dispose();
        lock (gate)
        {
            if (shown && error is not null)
            {
                error.Write($"\r{new string(' ', drawn)}\r");
                error.Flush();
            }

            shown = false;
        }
    }

    private void Tick()
    {
        lock (gate)
        {
            if (step.Length == 0)
                return;
            shown = true;
            Draw();
        }
    }

    /// <summary>Writes the line over the one before, within the terminal's width, padding over what a longer one left.</summary>
    private void Draw()
    {
        if (error is null)
            return;
        var seconds = (int)terminal.Clock.GetElapsedTime(started).TotalSeconds;
        var line = string.Create(CultureInfo.InvariantCulture, $"haruspex: {step} ({seconds}s)");
        var width = Math.Max(terminal.Width - 1, 10);
        if (line.Length > width)
            line = line[..(width - 1)] + "…";
        error.Write($"\r{line.PadRight(drawn)}");
        error.Flush();
        drawn = line.Length;
    }
}
