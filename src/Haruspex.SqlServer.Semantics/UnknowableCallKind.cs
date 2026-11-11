namespace Haruspex.SqlServer.Semantics;

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
