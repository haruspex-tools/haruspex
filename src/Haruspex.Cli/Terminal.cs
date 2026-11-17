using System.Collections;
using System.Reflection;

namespace Haruspex.Cli;

/// <summary>
/// What the command knows of where it runs, read once when it starts and passed in: whether standard output and standard error
/// are terminals, the terminal's width, the environment's variables, the clock and the tool's version. Everything the output
/// depends on beyond the command line is here, so every way the output can look is tested without a terminal, and a test that
/// runs inside CI (where <c>CI</c> and <c>GITHUB_ACTIONS</c> are set) prints what it prints anywhere else.
/// </summary>
internal sealed class Terminal
{
    /// <summary>Whether standard output is a terminal rather than a file or a pipe.</summary>
    public bool OutputIsTerminal { get; init; }

    /// <summary>Whether standard error is a terminal.</summary>
    public bool ErrorIsTerminal { get; init; }

    /// <summary>
    /// Whether standard output, when a terminal, shows escape sequences (colour, links) rather than printing them: always
    /// elsewhere, and on Windows when its console's processing of them is on or could be turned on (<see cref="Output.WindowsConsole"/>).
    /// </summary>
    public bool OutputShowsEscapes { get; init; } = true;

    /// <summary>Whether standard error, when a terminal, shows escape sequences (<see cref="OutputShowsEscapes"/>).</summary>
    public bool ErrorShowsEscapes { get; init; } = true;

    /// <summary>How many columns the terminal has, which a progress line keeps within; 80 when not known.</summary>
    public int Width { get; init; } = 80;

    /// <summary>The environment's variables, by name.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();

    /// <summary>The clock a progress line waits on.</summary>
    public TimeProvider Clock { get; init; } = TimeProvider.System;

    /// <summary>The tool's version, as <c>--version</c> prints it and SARIF records it.</summary>
    public string Version { get; init; } = ToolVersion;

    /// <summary>The version this build of haruspex carries: the assembly's informational version, which the build stamps with the commit.</summary>
    /// <remarks>Declared before <see cref="Plain"/>, whose version it is: static properties are set in the order they are written.</remarks>
    public static string ToolVersion { get; } =
        typeof(Terminal).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    /// <summary>
    /// Neither stream a terminal, and no variable set: how the tests run the command unless they say otherwise, and how a
    /// script or a CI job that sets nothing sees it.
    /// </summary>
    public static Terminal Plain { get; } = new();

    /// <summary>
    /// The value of the variable <paramref name="name"/>, or null when it isn't set or is empty: <c>NO_COLOR</c>'s convention,
    /// that an empty value is as good as none, held for every variable the command reads.
    /// </summary>
    public string? Variable(string name) => Environment.TryGetValue(name, out var value) && value.Length > 0 ? value : null;

    /// <summary>The process's own console and environment.</summary>
    public static Terminal FromConsole()
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
        {
            if (entry is { Key: string key, Value: string value })
                variables[key] = value;
        }

        var outputIsTerminal = !Console.IsOutputRedirected;
        var errorIsTerminal = !Console.IsErrorRedirected;
        return new Terminal
        {
            OutputIsTerminal = outputIsTerminal,
            ErrorIsTerminal = errorIsTerminal,
            OutputShowsEscapes = !OperatingSystem.IsWindows() || !outputIsTerminal || Output.WindowsConsole.ShowsEscapes(Output.WindowsConsole.Output),
            ErrorShowsEscapes = !OperatingSystem.IsWindows() || !errorIsTerminal || Output.WindowsConsole.ShowsEscapes(Output.WindowsConsole.Error),
            Width = Columns(),
            Environment = variables,
        };
    }

    /// <summary>The console's width, or 80 when it has none (a redirected stream, a console that can't say).</summary>
    private static int Columns()
    {
        try
        {
            return Console.IsErrorRedirected || Console.WindowWidth <= 0 ? 80 : Console.WindowWidth;
        }
        catch (Exception e) when (e is IOException or PlatformNotSupportedException)
        {
            return 80;
        }
    }
}
