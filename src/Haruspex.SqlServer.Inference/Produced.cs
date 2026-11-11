using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Inference;

/// <summary>A result set one path returns at a position, from the statement at <paramref name="At"/>.</summary>
internal sealed record Produced(ResultShape Result, SourceLocation At);
