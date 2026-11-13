namespace Haruspex.Diagnostics;

/// <summary>A position in a source file.</summary>
/// <param name="File">The path relative to the source root, with <c>/</c> separators on every platform.</param>
/// <param name="Line">The 1-based line.</param>
/// <param name="Column">The 1-based column.</param>
public sealed record SourceLocation(string File, int Line, int Column) : DiagnosticLocation
{
    /// <summary><c>file:line:column</c>, the form compilers and editors recognise.</summary>
    public override string ToString() => $"{File}:{Line}:{Column}";
}
