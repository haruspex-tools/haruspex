using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Semantics;

/// <summary>An <c>EXEC</c> of a procedure of the project, which is the procedure that runs.</summary>
/// <param name="Procedure">The procedure.</param>
/// <param name="At">Where the <c>EXEC</c> is.</param>
public sealed record ProcedureCall(Procedure Procedure, SourceLocation At) : CallTarget(At);
