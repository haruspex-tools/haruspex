using System.Globalization;

namespace Haruspex.Cli.Output;

/// <summary>
/// How a run's report is written, settled once from the command line and the terminal: how much it says, whether each stream is
/// coloured and its codes linked, and whether a progress line is shown. Every rule that reads the environment is here, so each
/// is tested by giving a <see cref="Terminal"/>. Each rule's doc says the convention it follows; docs/command-line.md, "Colour,
/// links and progress", says them for the reader.
/// </summary>
/// <param name="detail">How much a text report says.</param>
/// <param name="output">How standard output is styled.</param>
/// <param name="error">How standard error is styled.</param>
/// <param name="progress">Whether to show a progress line on standard error.</param>
internal sealed class OutputSettings(OutputDetail detail, TextStyle output, TextStyle error, bool progress)
{
    /// <summary>How much a text report says.</summary>
    public OutputDetail Detail { get; } = detail;

    /// <summary>How standard output is styled.</summary>
    public TextStyle Output { get; } = output;

    /// <summary>How standard error is styled.</summary>
    public TextStyle Error { get; } = error;

    /// <summary>Whether to show a progress line on standard error while the run works.</summary>
    public bool Progress { get; } = progress;

    /// <summary>The settings for a run asked for <paramref name="detail"/> and <paramref name="colour"/>, in <paramref name="terminal"/>.</summary>
    public static OutputSettings Settle(OutputDetail detail, ColourChoice colour, Terminal terminal)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        return new OutputSettings(
            detail,
            Style(colour, terminal.OutputIsTerminal, terminal),
            Style(colour, terminal.ErrorIsTerminal, terminal),
            ShowsProgress(detail, terminal));
    }

    /// <summary>How a stream is styled: colour as <see cref="Coloured"/> settles it, and links only with colour, as <see cref="Linked"/> settles them.</summary>
    private static TextStyle Style(ColourChoice colour, bool isTerminal, Terminal terminal)
    {
        var coloured = Coloured(colour, isTerminal, terminal);
        return new TextStyle(coloured, coloured && Linked(isTerminal, terminal));
    }

    /// <summary>
    /// Whether a stream is coloured. The colour option set to <c>always</c> or <c>never</c> decides. Otherwise, in order: <c>FORCE_COLOR</c>, set to
    /// anything but <c>0</c> or <c>false</c>, colours, and set to one of those doesn't; <c>NO_COLOR</c>, set to anything, doesn't
    /// (the convention of that name); a terminal that says it is dumb (<c>TERM=dumb</c>) doesn't; and otherwise a stream is coloured when it is a
    /// terminal, never when it is a file or a pipe.
    /// </summary>
    internal static bool Coloured(ColourChoice colour, bool isTerminal, Terminal terminal) => colour switch
    {
        ColourChoice.Always => true,
        ColourChoice.Never => false,
        ColourChoice.Auto => terminal.Variable("FORCE_COLOR") is { } force
            ? !IsOff(force)
            : terminal.Variable("NO_COLOR") is null && !IsDumb(terminal) && isTerminal,
    };

    /// <summary>
    /// Whether a coloured stream's codes are links (OSC 8) to their pages. A terminal that doesn't support them prints the escape
    /// as text or drops it, and none says whether it does, so links are made only where the terminal is known to: set
    /// <c>FORCE_HYPERLINK</c> to decide (anything but <c>0</c> or <c>false</c> links); otherwise only on a terminal, never in CI,
    /// and only in a terminal that names itself as one that supports them (<see cref="KnownToLink"/>).
    /// </summary>
    internal static bool Linked(bool isTerminal, Terminal terminal) =>
        terminal.Variable("FORCE_HYPERLINK") is { } force
            ? !IsOff(force)
            : isTerminal && terminal.Variable("CI") is null && KnownToLink(terminal);

    /// <summary>
    /// Whether the terminal names itself as one that shows OSC 8 links: Windows Terminal (<c>WT_SESSION</c>), iTerm2, WezTerm,
    /// Visual Studio Code's terminal and Ghostty (<c>TERM_PROGRAM</c>), kitty, the terminals built on VTE 0.50 or later, such as
    /// GNOME Terminal (<c>VTE_VERSION</c> of 5000 or more), Konsole and DomTerm. The list is the one the supports-hyperlinks
    /// package keeps for Node, narrowed to the terminals that say so in their environment.
    /// </summary>
    internal static bool KnownToLink(Terminal terminal) =>
        terminal.Variable("WT_SESSION") is not null
        || terminal.Variable("TERM_PROGRAM") is "iTerm.app" or "WezTerm" or "vscode" or "ghostty"
        || terminal.Variable("TERM") is "xterm-kitty" or "xterm-ghostty" or "wezterm"
        || terminal.Variable("KITTY_WINDOW_ID") is not null
        || terminal.Variable("KONSOLE_VERSION") is not null
        || terminal.Variable("DOMTERM") is not null
        || (int.TryParse(terminal.Variable("VTE_VERSION"), NumberStyles.None, CultureInfo.InvariantCulture, out var vte) && vte >= 5000);

    /// <summary>
    /// Whether to show progress: only when standard error is a terminal, which can redraw a line, and never in CI, whose log would
    /// keep every frame; not with <c>--quiet</c>, nor in a terminal that says it is dumb.
    /// </summary>
    internal static bool ShowsProgress(OutputDetail detail, Terminal terminal) =>
        detail != OutputDetail.Quiet && terminal.ErrorIsTerminal && terminal.Variable("CI") is null && !IsDumb(terminal);

    private static bool IsDumb(Terminal terminal) => terminal.Variable("TERM") == "dumb";

    /// <summary>Whether a variable that forces something on is set to turn it off instead, as the convention has it: <c>0</c> or <c>false</c>.</summary>
    private static bool IsOff(string value) => value is "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase);
}
