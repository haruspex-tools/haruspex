using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Semantics;

/// <summary>What an <c>EXEC</c> runs: a procedure of the project, or something whose results the project cannot show.</summary>
/// <param name="At">Where the <c>EXEC</c> is.</param>
public abstract record CallTarget(SourceLocation At);
