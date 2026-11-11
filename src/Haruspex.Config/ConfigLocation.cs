namespace Haruspex.Config;

/// <summary>A place in the config file: the path to a value, and where it is written.</summary>
/// <param name="Path">The path to the value, such as <c>procedures["sales.get_order"].paths["lines[]"].why</c>.</param>
/// <param name="Line">The line, counted from 1.</param>
/// <param name="Column">The column, counted from 1 in characters.</param>
public sealed record ConfigLocation(string Path, int Line, int Column);
