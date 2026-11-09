namespace Haruspex.Diagnostics;

/// <summary>A position in a source file.</summary>
/// <param name="File">The path relative to the source root, with <c>/</c> separators on every platform.</param>
/// <param name="Line">The 1-based line.</param>
/// <param name="Column">The 1-based column.</param>
public sealed record SourceLocation(string File, int Line, int Column)
{
    /// <summary><c>file:line:column</c>, the form compilers and editors recognise.</summary>
    public override string ToString() => $"{File}:{Line}:{Column}";
}

/// <summary>An error in the source that stops analysis, such as a parse error.</summary>
/// <remarks>
/// The design gives a diagnostic a code and a severity too. Every diagnostic today is an error without a code,
/// so they are left for the slice that needs them rather than invented here (slice 2b moved this type only).
/// </remarks>
/// <param name="Message">What is wrong, in a sentence.</param>
/// <param name="At">Where, when it is known.</param>
public sealed record Diagnostic(string Message, SourceLocation? At)
{
    /// <summary><c>file:line:column: error: message</c>.</summary>
    public override string ToString() => At is null ? $"error: {Message}" : $"{At}: error: {Message}";
}
