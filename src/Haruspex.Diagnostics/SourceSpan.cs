namespace Haruspex.Diagnostics;

/// <summary>
/// Where an object is in its source: its file, where it starts and where it ends. It is captured once, when the object is
/// read, and every location reported in the object (a diagnostic, an <c>at</c> in the IR) is a position in its span's file,
/// so the file is never carried apart from it.
/// </summary>
/// <remarks>
/// A <see cref="SourceLocation"/> is a point, which is what a diagnostic reports; a span is the extent of something read,
/// from which points are taken (<see cref="Start"/>, <see cref="At"/>). Lines and columns are counted as ScriptDom counts
/// them: from 1, a tab as one column, and a line ended by CRLF, LF or a lone CR (observed in the plots fixture,
/// <c>tests/fixtures/plots/README.md</c>).
/// </remarks>
/// <param name="File">The path relative to the source root, with <c>/</c> separators on every platform.</param>
/// <param name="Line">The 1-based line it starts on.</param>
/// <param name="Column">The 1-based column it starts at.</param>
/// <param name="EndLine">The 1-based line it ends on.</param>
/// <param name="EndColumn">The 1-based column after its last character.</param>
public sealed record SourceSpan(string File, int Line, int Column, int EndLine, int EndColumn)
{
    /// <summary>Where it starts.</summary>
    public SourceLocation Start => new(File, Line, Column);

    /// <summary>A position in the same file, such as that of a statement inside the object.</summary>
    /// <param name="line">The 1-based line.</param>
    /// <param name="column">The 1-based column.</param>
    public SourceLocation At(int line, int column) => new(File, line, column);

    /// <summary>
    /// Orders spans as their objects appear in the source: by file, ordinally, then by where they start. It is the order the
    /// model keeps its objects in.
    /// </summary>
    public static IComparer<SourceSpan> InSourceOrder { get; } = Comparer<SourceSpan>.Create((a, b) =>
    {
        var file = string.CompareOrdinal(a.File, b.File);
        return file != 0 ? file : a.Line != b.Line ? a.Line.CompareTo(b.Line) : a.Column.CompareTo(b.Column);
    });

    /// <summary>Where it starts, as <c>file:line:column</c>.</summary>
    public override string ToString() => Start.ToString();
}
