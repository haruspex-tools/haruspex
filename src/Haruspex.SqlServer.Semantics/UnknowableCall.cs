using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Semantics;

/// <summary>An <c>EXEC</c> whose results cannot be known from the project.</summary>
/// <param name="Kind">Why.</param>
/// <param name="Name">
/// The procedure's two-part name, by which a declaration of its results can be found: as written, with an unqualified
/// <c>sp_…</c> or <c>xp_…</c> name in <c>sys</c> and any other in <c>dbo</c>. Null when the call has no such name (dynamic
/// SQL, a variable, a procedure in another database or on another server).
/// </param>
/// <param name="Detail">A sentence saying what was found.</param>
/// <param name="At">Where the <c>EXEC</c> is.</param>
public sealed record UnknowableCall(UnknowableCallKind Kind, ObjectName? Name, string Detail, SourceLocation At) : CallTarget(At);
