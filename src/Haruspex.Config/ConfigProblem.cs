using System.Globalization;

namespace Haruspex.Config;

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
    /// <remarks>Without a config file (an empty <see cref="File"/>), it is written as the command line's own problems are, <c>haruspex: error: message</c>.</remarks>
    public override string ToString()
    {
        var severity = Severity == ConfigSeverity.Error ? "error" : "warning";
        if (File.Length == 0)
            return $"haruspex: {severity}: {Message}";
        return At is { } at
            ? string.Create(CultureInfo.InvariantCulture, $"{File}:{at.Line}:{at.Column}: {severity}: {(at.Path.Length == 0 ? "" : at.Path + ": ")}{Message}")
            : $"{File}: {severity}: {Message}";
    }
}
