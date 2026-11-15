namespace Haruspex.Ir;

/// <summary>A reason a value or a result cannot be typed. Its name in the IR's JSON is in <see cref="IrWireNames"/>.</summary>
public enum IrUnknownReason
{
    /// <summary>A statement that is not modelled yet, which may return results of its own.</summary>
    UnsupportedStatement,

    /// <summary>Results produced by code outside the project, such as a CLR procedure.</summary>
    ExternalCode,

    /// <summary>
    /// An object defined more than once by statements whose order decides which applies, such as a CREATE
    /// and an ALTER PROCEDURE, or a definition followed by a DROP.
    /// </summary>
    Redefined,

    /// <summary>A query form that is not modelled yet, such as a CTE or <c>UNION</c>.</summary>
    UnsupportedQuery,

    /// <summary>An expression or function that is not modelled yet.</summary>
    UnsupportedExpression,

    /// <summary>A table source or join that is not modelled yet.</summary>
    UnsupportedSource,

    /// <summary>A SQL type that is not modelled yet.</summary>
    UnsupportedSqlType,

    /// <summary>A table that is not defined in the project.</summary>
    UnresolvedTable,

    /// <summary>A column that cannot be found, or is ambiguous, in the tables in scope.</summary>
    UnresolvedColumn,

    /// <summary>A result column without a name.</summary>
    UnnamedColumn,

    /// <summary>Two result columns that claim the same key.</summary>
    DuplicateColumn,

    /// <summary>
    /// Functions that call themselves, directly or through others, whose shape reached no fixed point in the bounded
    /// number of steps, or that never return a value (every return calls itself again).
    /// </summary>
    UnresolvedRecursion,

    /// <summary>JSON whose shape is not visible here, such as JSON text read from a column.</summary>
    JsonShapeUnknown,

    /// <summary>
    /// A temporary table the procedure does not create on every path before it reads it: a table of that name created by
    /// the caller, if there is one, whose shape depends on who calls.
    /// </summary>
    CallerTempTable,

    /// <summary>
    /// Result sets that cannot be listed because which are returned depends on the path through the procedure in a way the
    /// list cannot express: a result inside a loop, or results of different encodings at one position.
    /// </summary>
    VaryingResults,

    /// <summary>Dynamic SQL (<c>EXEC (@sql)</c>, <c>sp_executesql</c>): what it runs is a string built when the procedure runs.</summary>
    DynamicSql,

    /// <summary><c>EXEC @name</c>: the procedure is chosen when the call runs.</summary>
    ProcedureVariable,

    /// <summary>
    /// A system procedure (<c>sp_…</c>, <c>xp_…</c>), whose results are SQL Server's own. A procedure of the project with such a
    /// name may be one too: SQL Server runs its own procedure of that name instead, if it has one.
    /// </summary>
    SystemProcedure,

    /// <summary>A procedure that is not in the project: in another database, on a linked server, or nowhere.</summary>
    MissingProcedure,

    /// <summary>An unqualified procedure name that more than one schema has: which runs depends on the caller's default schema.</summary>
    AmbiguousProcedure,

    /// <summary>
    /// A statement fires a trigger of the project that may return result sets of its own, which arrive among the
    /// procedure's. SQL Server allows it unless the server's <c>disallow results from triggers</c> option is on, which it is
    /// not by default.
    /// </summary>
    TriggerResults,

    /// <summary>
    /// A result set carries a column the server marks hidden, which some drivers list among the columns and others leave
    /// out, so its keys depend on the client: <c>FETCH</c> without <c>INTO</c>, whose row has <c>ROWSTAT</c>.
    /// </summary>
    HiddenColumn,

    /// <summary>
    /// A module created <c>WITH ENCRYPTION</c> whose definition the source doesn't hold: a DACPAC extracted from a database keeps
    /// a placeholder in its place, since the server gives no one the text (0.19). What it returns can only be declared.
    /// </summary>
    EncryptedDefinition,

    /// <summary>
    /// A literal or a name holding a SQLCMD variable, <c>$(Region)</c>, that a deployment replaces with the variable's value before
    /// the server sees the text (0.21). The value may have any length, the empty one too, and may even end the string, so nothing
    /// drawn from the written text holds: not its length, its value, nor its type.
    /// </summary>
    SqlcmdVariable,
}
