namespace Haruspex.Ir;

/// <summary>The reasons a value or a result cannot be typed.</summary>
public static class IrUnknownReasons
{
    /// <summary>A statement that is not modelled yet, which may return results of its own.</summary>
    public const string UnsupportedStatement = "unsupported-statement";

    /// <summary>Results produced by code outside the project, such as a CLR procedure.</summary>
    public const string ExternalCode = "external-code";

    /// <summary>
    /// An object defined more than once by statements whose order decides which applies, such as a CREATE
    /// and an ALTER PROCEDURE, or a definition followed by a DROP.
    /// </summary>
    public const string Redefined = "redefined";

    /// <summary>A query form that is not modelled yet, such as a CTE or <c>UNION</c>.</summary>
    public const string UnsupportedQuery = "unsupported-query";

    /// <summary>An expression or function that is not modelled yet.</summary>
    public const string UnsupportedExpression = "unsupported-expression";

    /// <summary>A table source or join that is not modelled yet.</summary>
    public const string UnsupportedSource = "unsupported-source";

    /// <summary>A SQL type that is not modelled yet.</summary>
    public const string UnsupportedSqlType = "unsupported-sql-type";

    /// <summary>A table that is not defined in the project.</summary>
    public const string UnresolvedTable = "unresolved-table";

    /// <summary>A column that cannot be found, or is ambiguous, in the tables in scope.</summary>
    public const string UnresolvedColumn = "unresolved-column";

    /// <summary>A result column without a name.</summary>
    public const string UnnamedColumn = "unnamed-column";

    /// <summary>Two result columns that claim the same key.</summary>
    public const string DuplicateColumn = "duplicate-column";

    /// <summary>
    /// Functions that call themselves, directly or through others, whose shape reached no fixed point in the bounded
    /// number of steps, or that never return a value (every return calls itself again).
    /// </summary>
    public const string UnresolvedRecursion = "unresolved-recursion";

    /// <summary>JSON whose shape is not visible here, such as JSON text read from a column.</summary>
    public const string JsonShapeUnknown = "json-shape-unknown";

    /// <summary>
    /// A temporary table the procedure does not create on every path before it reads it: a table of that name created by
    /// the caller, if there is one, whose shape depends on who calls.
    /// </summary>
    public const string CallerTempTable = "caller-temp-table";

    /// <summary>
    /// Result sets that cannot be listed because which are returned depends on the path through the procedure in a way the
    /// list cannot express: a result inside a loop, or results of different encodings at one position.
    /// </summary>
    public const string VaryingResults = "varying-results";

    /// <summary>Dynamic SQL (<c>EXEC (@sql)</c>, <c>sp_executesql</c>): what it runs is a string built when the procedure runs.</summary>
    public const string DynamicSql = "dynamic-sql";

    /// <summary><c>EXEC @name</c>: the procedure is chosen when the call runs.</summary>
    public const string ProcedureVariable = "procedure-variable";

    /// <summary>
    /// A system procedure (<c>sp_…</c>, <c>xp_…</c>), whose results are SQL Server's own. A procedure of the project with such a
    /// name may be one too: SQL Server runs its own procedure of that name instead, if it has one.
    /// </summary>
    public const string SystemProcedure = "system-procedure";

    /// <summary>A procedure that is not in the project: in another database, on a linked server, or nowhere.</summary>
    public const string MissingProcedure = "missing-procedure";

    /// <summary>An unqualified procedure name that more than one schema has: which runs depends on the caller's default schema.</summary>
    public const string AmbiguousProcedure = "ambiguous-procedure";

    /// <summary>
    /// A statement fires a trigger of the project that may return result sets of its own, which arrive among the
    /// procedure's. SQL Server allows it unless the server's <c>disallow results from triggers</c> option is on, which it is
    /// not by default.
    /// </summary>
    public const string TriggerResults = "trigger-results";

    /// <summary>
    /// A result set carries a column the server marks hidden, which some drivers list among the columns and others leave
    /// out, so its keys depend on the client: <c>FETCH</c> without <c>INTO</c>, whose row has <c>ROWSTAT</c>.
    /// </summary>
    public const string HiddenColumn = "hidden-column";
}
