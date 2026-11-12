namespace Haruspex.Ir;

/// <summary>A reason a value can be null. Its name in the IR's JSON is in <see cref="IrWireNames"/>.</summary>
/// <remarks>
/// Each names the reason, never the SQL that causes it, so that a front end for any dialect can give it: the construct is
/// where the reason's <see cref="IrNullReason.At"/> points. The examples below are SQL Server's, which the SQL Server front
/// end gives each reason for; its README lists them all (<c>docs/decisions/0025-null-reasons-in-neutral-terms.md</c>).
/// </remarks>
public enum IrNullRule
{
    /// <summary>The column it reads is declared nullable (or not declared <c>NOT NULL</c>).</summary>
    NullableColumn,

    /// <summary>
    /// It comes from the optional side of a join, which has no row when nothing matches: a left join's right side, either side
    /// of a full join, or a lateral join's query that returns no row for an outer row (SQL Server's <c>OUTER APPLY</c>). A
    /// lateral query that always returns a row (an aggregate without <c>GROUP BY</c>, a query without <c>FROM</c>, a
    /// <c>VALUES</c> list) never gives it.
    /// </summary>
    UnmatchedRow,

    /// <summary>It is a subquery, which is null when it returns no rows.</summary>
    EmptySubquery,

    /// <summary>It is a nested object that is left out when every one of its fields is null.</summary>
    AllFieldsNull,

    /// <summary>It is an aggregate (<c>MIN</c>, <c>MAX</c>, <c>json_arrayagg</c>, <c>json_objectagg</c>) without GROUP BY, which is null over no rows.</summary>
    EmptyAggregate,

    /// <summary>It is a scalar function's result, typed from the declared return type only, so it may be NULL.</summary>
    FunctionResult,

    /// <summary>It is a function's parameter, which a caller may pass as NULL.</summary>
    Parameter,

    /// <summary>
    /// It is a call of a function declared <c>WITH RETURNS NULL ON NULL INPUT</c> with an argument that may be null: such
    /// a call is NULL without running the body.
    /// </summary>
    NullInput,

    /// <summary>It is a query result, which returns nothing when no rows match.</summary>
    NoRows,

    /// <summary>
    /// It is a result whose statement may not run (it is in a branch, or after a <c>RETURN</c> that may be taken), so the
    /// result set may be missing altogether rather than empty. Only a result after which every result may be missing too
    /// has it, so the result sets a client receives are always the first ones listed.
    /// </summary>
    NotRun,

    /// <summary>It is a procedure's variable, which holds null until a value that is never null is assigned on every path.</summary>
    Variable,

    /// <summary>
    /// It is a <c>COUNT</c>, an <c>int</c>, which over more than 2,147,483,647 rows overflows: an error by default, but NULL
    /// when the caller's session has <c>ANSI_WARNINGS</c> and <c>ARITHABORT</c> off, which a procedure cannot prevent.
    /// <c>COUNT_BIG</c> does not.
    /// </summary>
    CountOverflow,

    /// <summary>
    /// It is arithmetic, a <c>SUM</c> or <c>AVG</c>, a numeric function (<c>ROUND</c>, <c>ABS</c>, <c>POWER</c>, <c>EXP</c>, …) or a
    /// conversion that can overflow its type or divide by zero: an error by default, but NULL when the caller's session has
    /// <c>ANSI_WARNINGS</c> and <c>ARITHABORT</c> off, which a procedure cannot prevent.
    /// <c>COALESCE</c> or <c>ISNULL</c> with a value never null removes it.
    /// </summary>
    ArithmeticOverflow,

    /// <summary>It is <c>TRY_CAST</c> or <c>TRY_CONVERT</c> of a value that may not convert, which gives NULL instead of an error.</summary>
    FailedConversion,

    /// <summary>
    /// It is a grouped column, and the query adds rows that total over it (super-aggregate rows: <c>ROLLUP</c>, <c>CUBE</c>,
    /// <c>GROUPING SETS</c>), which hold NULL in the columns they roll up, whatever the filters proved of them.
    /// </summary>
    RolledUp,

    /// <summary>
    /// It is a column a statement that takes several actions reports, which has no value for some of them: in SQL Server's
    /// <c>MERGE</c> ... <c>OUTPUT</c>, a <c>deleted</c> column for the rows it inserts, an <c>inserted</c> column for the rows it
    /// deletes, and a column of its source for the rows it changes because they are not matched by the source.
    /// </summary>
    MissingForAction,

    /// <summary>It is a choice with no branch for when no condition holds (a <c>CASE</c> without <c>ELSE</c>), so NULL then.</summary>
    NoBranchMatched,

    /// <summary>
    /// A result of a <c>CASE</c>, <c>IIF</c> or <c>CHOOSE</c> it may give is the <c>NULL</c> literal, or it is (or reads) the
    /// <c>NULL</c> literal itself, as a bare <c>NULL</c> select item (an <c>int</c>) or <c>CAST(NULL AS date)</c> (0.14).
    /// </summary>
    NullLiteral,

    /// <summary>It is a <c>CHOOSE</c> whose index may be below 1 or above its count of values, which gives NULL.</summary>
    OutOfRange,

    /// <summary>
    /// It is a <c>COALESCE</c> one of whose arguments, a subquery or a call, is tested and then read again: the second read may
    /// be NULL (another statement changed the rows between them, or the call gives another value). Before 0.12 the argument's
    /// own reasons were given instead. No seed reaches it; it is observed.
    /// </summary>
    ReadTwice,

    /// <summary>It is made NULL when it equals a given value (<c>NULLIF</c>, NULL when its two arguments compare equal).</summary>
    NullWhenEqual,

    /// <summary>
    /// It is read from JSON at a path, and the JSON holds <c>null</c> there or, read leniently, nothing or the wrong kind of
    /// value. In SQL Server: a column <c>OPENJSON</c> reads (0.13), or a <c>JSON_VALUE</c> (0.14), NULL where the JSON holds
    /// <c>null</c>, under a <c>strict</c> path too, and, under a lax path, where nothing is there, or an <c>AS JSON</c> column
    /// finds a scalar, or a scalar column or <c>JSON_VALUE</c> an object or array (or, for <c>JSON_VALUE</c>, a string longer
    /// than 4,000 characters). (A number too large for the column is NULL as well under the caller's <c>ANSI_WARNINGS</c> and
    /// <c>ARITHABORT</c> off.)
    /// </summary>
    JsonNullOrAbsent,

    /// <summary>
    /// It is a window function over a frame that may hold no row (0.14): an aggregate, <c>FIRST_VALUE</c> or
    /// <c>LAST_VALUE</c> whose <c>ROWS</c> frame may leave out the current row (<c>ROWS BETWEEN 2 PRECEDING AND 1 PRECEDING</c>
    /// has nothing before the first row) is NULL there, over a column never null too. The default frame, and any frame that
    /// holds the current row, is never empty.
    /// </summary>
    EmptyFrame,

    /// <summary>
    /// It is <c>LAG</c> or <c>LEAD</c> without a default (0.14), NULL where the row it reads is past the partition's edge.
    /// </summary>
    WindowEdge,

    /// <summary>
    /// It is a math function that may be given a value outside its domain (0.15): <c>SQRT</c> of a negative, <c>LOG</c> or
    /// <c>LOG10</c> of 0 or less or to a base of 0 or 1, <c>POWER</c> of a negative to a fractional power. That is an invalid
    /// floating point operation: an error by default, but NULL when the caller's session has <c>ANSI_WARNINGS</c> and
    /// <c>ARITHABORT</c> off, which a procedure cannot prevent, as for <see cref="ArithmeticOverflow"/>.
    /// </summary>
    DomainError,

    /// <summary>
    /// It is a sample statistic, <c>STDEV</c> or <c>VAR</c> (0.15), which is NULL over fewer than two values that are not NULL:
    /// a group or a window's frame of one row, with <c>GROUP BY</c> too. The population forms, <c>STDEVP</c> and <c>VARP</c>,
    /// are 0 over one value.
    /// </summary>
    TooFewValues,

    /// <summary>Its type is unknown, so nothing is proved about it.</summary>
    Unknown,

    /// <summary>
    /// The config file declares it may be null, or absent: in results declared for a procedure whose results cannot be
    /// inferred. <see cref="IrNullReason.At"/> is the procedure declared.
    /// </summary>
    Declared,
}
