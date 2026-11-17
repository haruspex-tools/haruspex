namespace Haruspex.Cli.Output;

/// <summary>
/// How text written to one stream is styled: with colour (ANSI's select graphic rendition) or without, and with terminal
/// hyperlinks (OSC 8) or without. A few escape sequences, written here rather than taken from a console library, which would be
/// a dependency, and one that Native AOT must be able to compile. Without colour, every method gives its text back unchanged,
/// so plain output is the same bytes whichever way it was asked for.
/// </summary>
/// <param name="Colour">Whether to colour.</param>
/// <param name="Links">Whether to make a diagnostic code a hyperlink to its page.</param>
internal readonly record struct TextStyle(bool Colour, bool Links)
{
    /// <summary>No colour and no links: output to a file, a pipe or a CI log.</summary>
    public static TextStyle Plain { get; } = new(Colour: false, Links: false);

    /// <summary>An error's severity word and code: bold red.</summary>
    public string Error(string text) => Sgr(text, "1;31");

    /// <summary>A warning's: bold yellow.</summary>
    public string Warning(string text) => Sgr(text, "1;33");

    /// <summary>A note's: cyan, quieter than the other two.</summary>
    public string Note(string text) => Sgr(text, "36");

    /// <summary>What the eye should find first: bold.</summary>
    public string Strong(string text) => Sgr(text, "1");

    /// <summary>What is there to be found rather than read: faint.</summary>
    public string Faint(string text) => Sgr(text, "2");

    /// <summary>Something added: green.</summary>
    public string Added(string text) => Sgr(text, "32");

    /// <summary>Something removed: red.</summary>
    public string Removed(string text) => Sgr(text, "31");

    /// <summary>Something changed: yellow.</summary>
    public string Changed(string text) => Sgr(text, "33");

    /// <summary>
    /// <paramref name="text"/> as a hyperlink to <paramref name="url"/>, in the form terminals that support them read
    /// (<c>ESC ] 8 ; ; url ESC \ text ESC ] 8 ; ; ESC \</c>); the text alone otherwise.
    /// </summary>
    public string Link(string text, string url) => Links ? $"\e]8;;{url}\e\\{text}\e]8;;\e\\" : text;

    /// <summary>The text between a select graphic rendition and its reset, when colouring.</summary>
    private string Sgr(string text, string code) => Colour && text.Length > 0 ? $"\e[{code}m{text}\e[0m" : text;
}
