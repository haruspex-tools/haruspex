using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// The table is not there: it was dropped at <paramref name="At"/>. A read of it fails the call (error 208, observed not
/// to be caught by a CATCH in the same procedure), so it never gives a value; a join with any other state is unknown.
/// </summary>
internal sealed record AbsentTable(SourceLocation At) : TempTable;
