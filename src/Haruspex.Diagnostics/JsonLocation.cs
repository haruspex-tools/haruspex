namespace Haruspex.Diagnostics;

/// <summary>
/// A value in a JSON document, such as a setting in a config file: the file, the path to the value, and where it is
/// written. The path names the value as the document's reader knows it, and the line and column let an editor go there.
/// </summary>
/// <param name="File">The document, as it was found or named.</param>
/// <param name="Path">The path to the value, such as <c>procedures["sales.get_order"].paths["lines[]"].why</c>; empty for the document's root.</param>
/// <param name="Line">The line, counted from 1.</param>
/// <param name="Column">The column, counted from 1 in characters.</param>
public sealed record JsonLocation(string File, string Path, int Line, int Column) : DiagnosticLocation;
