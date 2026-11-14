namespace Haruspex.Diagnostics;

/// <summary>A position in a source file.</summary>
/// <param name="File">The path relative to the source root, with <c>/</c> separators on every platform.</param>
/// <param name="Line">The 1-based line.</param>
/// <param name="Column">The 1-based column.</param>
public sealed record SourceLocation(string File, int Line, int Column) : DiagnosticLocation
{
    /// <summary><c>file:line:column</c>, the form compilers and editors recognise.</summary>
    public override string ToString() => $"{File}:{Line}:{Column}";

    /// <summary>
    /// Reads <c>file:line:column</c>, as <see cref="ToString"/> writes it (the IR holds locations so). The file may hold
    /// colons; the line and the column are the last two parts, each from 1.
    /// </summary>
    /// <returns>Whether <paramref name="text"/> is such a location.</returns>
    public static bool TryParse(string text, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SourceLocation? location)
    {
        ArgumentNullException.ThrowIfNull(text);
        location = null;
        var lastColon = text.LastIndexOf(':');
        var columnColon = lastColon > 0 ? text.LastIndexOf(':', lastColon - 1) : -1;
        if (columnColon <= 0
            || !int.TryParse(text.AsSpan(columnColon + 1, lastColon - columnColon - 1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var line)
            || !int.TryParse(text.AsSpan(lastColon + 1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var column)
            || line < 1 || column < 1)
        {
            return false;
        }

        location = new SourceLocation(text[..columnColon], line, column);
        return true;
    }
}
