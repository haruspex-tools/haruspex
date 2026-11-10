using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Semantics;

/// <summary>What an <c>EXEC</c> runs: a procedure of the project, or something whose results the project cannot show.</summary>
/// <param name="At">Where the <c>EXEC</c> is.</param>
public abstract record CallTarget(SourceLocation At);

/// <summary>An <c>EXEC</c> of a procedure of the project, which is the procedure that runs.</summary>
/// <param name="Procedure">The procedure.</param>
/// <param name="At">Where the <c>EXEC</c> is.</param>
public sealed record ProcedureCall(Procedure Procedure, SourceLocation At) : CallTarget(At);

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

/// <summary>Why the results of an <see cref="UnknowableCall"/> cannot be known.</summary>
public enum UnknowableCallKind
{
    /// <summary><c>EXEC (@sql)</c>, <c>EXEC (N'…')</c> or <c>sp_executesql</c>: the SQL is a string, built when it runs.</summary>
    DynamicSql,

    /// <summary><c>EXEC @name</c>: the procedure is chosen when the call runs.</summary>
    ProcedureVariable,

    /// <summary>
    /// A name starting <c>sp_</c> or <c>xp_</c>. SQL Server runs its own procedure of that name if it has one, whatever the
    /// schema written and whatever the database defines (observed), and which it has is not known here.
    /// </summary>
    SystemProcedure,

    /// <summary>No procedure of the project has the name: another database, a linked server, a synonym, or none at all.</summary>
    MissingProcedure,

    /// <summary>An unqualified name that more than one schema of the project has a procedure of.</summary>
    AmbiguousProcedure,
}
