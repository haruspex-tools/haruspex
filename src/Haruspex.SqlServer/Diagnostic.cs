namespace Haruspex.SqlServer;

/// <summary>A position in a source file.</summary>
/// <param name="File">The path relative to the source root, with <c>/</c> separators on every platform.</param>
/// <param name="Line">The 1-based line.</param>
/// <param name="Column">The 1-based column.</param>
public sealed record SourceLocation(string File, int Line, int Column)
{
    public override string ToString() => $"{File}:{Line}:{Column}";
}

/// <summary>An error in the source that stops analysis, such as a parse error.</summary>
public sealed record Diagnostic(string Message, SourceLocation? At)
{
    public override string ToString() => At is null ? $"error: {Message}" : $"{At}: error: {Message}";
}
