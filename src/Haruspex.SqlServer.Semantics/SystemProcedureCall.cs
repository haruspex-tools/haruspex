using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// An <c>EXEC</c> of one of SQL Server's own procedures that the catalogue lists (<see cref="SystemProcedures"/>), which runs
/// whatever schema is written and whatever the project defines.
/// </summary>
/// <param name="Procedure">The catalogue's entry.</param>
/// <param name="At">Where the <c>EXEC</c> is.</param>
public sealed record SystemProcedureCall(SystemProcedure Procedure, SourceLocation At) : CallTarget(At);
