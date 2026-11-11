using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// No value at all: what a recursive call gives before the fixed point has learnt anything about it. It never reaches
/// the IR; a function whose result is still this when the fixed point is reached never returns.
/// </summary>
/// <param name="At">The function's definition.</param>
internal sealed record NeverShape(SourceLocation At) : Shape;
