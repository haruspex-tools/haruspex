using System.Globalization;

namespace Haruspex.Config;

/// <summary>Whether a problem stops generation.</summary>
public enum ConfigSeverity
{
    /// <summary>Nothing is generated.</summary>
    Error,

    /// <summary>Generation goes ahead; the config file could be simpler (an override the inference now agrees with).</summary>
    Warning,
}

/// <summary>A place in the config file: the path to a value, and where it is written.</summary>
/// <param name="Path">The path to the value, such as <c>procedures["sales.get_order"].paths["lines[]"].why</c>.</param>
/// <param name="Line">The line, counted from 1.</param>
/// <param name="Column">The column, counted from 1 in characters.</param>
public sealed record ConfigLocation(string Path, int Line, int Column);

/// <summary>Something wrong, or worth simplifying, in a config file.</summary>
/// <param name="Severity">Whether it stops generation.</param>
/// <param name="File">The config file, as it was found or named.</param>
/// <param name="At">Where in it, when the problem has a place; null for one about the file as a whole, such as a clash between generated names.</param>
/// <param name="Message">What is wrong, in a sentence or two.</param>
public sealed record ConfigProblem(ConfigSeverity Severity, string File, ConfigLocation? At, string Message)
{
    /// <summary>
    /// The problem as a compiler writes one, <c>file:line:column: error: path: message</c>, so editors and CI annotations
    /// can jump to it.
    /// </summary>
    public override string ToString()
    {
        var severity = Severity == ConfigSeverity.Error ? "error" : "warning";
        return At is { } at
            ? string.Create(CultureInfo.InvariantCulture, $"{File}:{at.Line}:{at.Column}: {severity}: {(at.Path.Length == 0 ? "" : at.Path + ": ")}{Message}")
            : $"{File}: {severity}: {Message}";
    }
}
