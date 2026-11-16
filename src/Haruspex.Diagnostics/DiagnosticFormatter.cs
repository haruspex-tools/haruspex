using System.Globalization;

namespace Haruspex.Diagnostics;

/// <summary>
/// Writes a diagnostic as one line, in the form compilers write and editors and CI annotate:
/// <c>file:line:column: error HAR1001: message</c>. Every diagnostic haruspex reports is written here, so the form is
/// stated once.
/// </summary>
/// <remarks>
/// The forms, by where the problem is:
/// <list type="bullet">
/// <item>a SQL file: <c>root/procedures/get_order.sql:7:5: error HAR1001: message</c>, the file joined to the source root;</item>
/// <item>a value in a JSON document: <c>haruspex.json:4:14: error HAR3004: objects: message</c>, the path to the value before the message;</item>
/// <item>a file or folder as a whole: <c>haruspex.json: error HAR4301: message</c>;</item>
/// <item>nowhere in particular: <c>haruspex: error HAR6001: message</c>.</item>
/// </list>
/// The severity is <c>error</c>, <c>warning</c> or <c>note</c>, and the code follows it, as tsc writes
/// <c>error TS2322:</c>.
/// </remarks>
public static class DiagnosticFormatter
{
    /// <summary>The diagnostic as one line.</summary>
    /// <param name="diagnostic">The diagnostic.</param>
    /// <param name="sourceRoot">The folder a SQL file's path is relative to, joined in front of it; empty to keep the path as held.</param>
    public static string Format(Diagnostic diagnostic, string sourceRoot = "")
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        ArgumentNullException.ThrowIfNull(sourceRoot);
        var head = $"{Word(diagnostic.Severity)} {diagnostic.Code}";
        return diagnostic.At switch
        {
            SourceLocation at => string.Create(CultureInfo.InvariantCulture, $"{PathAsShown(Path.Join(sourceRoot, at.File))}:{at.Line}:{at.Column}: {head}: {diagnostic.Message}"),
            JsonLocation at => string.Create(CultureInfo.InvariantCulture, $"{PathAsShown(at.File)}:{at.Line}:{at.Column}: {head}: {(at.Path.Length == 0 ? "" : at.Path + ": ")}{diagnostic.Message}"),
            FileLocation at => $"{PathAsShown(at.File)}: {head}: {diagnostic.Message}",
            null => $"haruspex: {head}: {diagnostic.Message}",
        };
    }

    /// <summary>
    /// A path as haruspex prints it: with <c>/</c> between its names on every platform, so that a diagnostic or a summary reads the
    /// same on Windows as elsewhere, as the IR's own locations do. Only Windows writes <c>\</c> as a separator; elsewhere it is a
    /// character a name may hold, and is kept. It is for every path haruspex makes, in a message too, such as a place it looked; a
    /// path quoted from a project, a package or a config file is printed as written there instead, on every platform.
    /// </summary>
    /// <param name="path">A path as the platform writes it.</param>
    public static string PathAsShown(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return OperatingSystem.IsWindows() ? path.Replace('\\', '/') : path;
    }

    private static string Word(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => "error",
        DiagnosticSeverity.Warning => "warning",
        DiagnosticSeverity.Note => "note",
    };
}
