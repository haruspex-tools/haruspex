namespace Haruspex.Ir;

/// <summary>The reasons a value can be null.</summary>
public static class IrNullRules
{
    /// <summary>The column it reads is declared nullable (or not declared <c>NOT NULL</c>).</summary>
    public const string NullableColumn = "nullable-column";

    /// <summary>
    /// It comes from the optional side of an outer join, which has no row when nothing matches: a left join's right side, or
    /// either side of a full join (0.13).
    /// </summary>
    public const string OuterJoin = "outer-join";

    /// <summary>
    /// It comes from the right side of an <c>OUTER APPLY</c>, which has no row for an outer row when the applied query returns
    /// none. An applied query that always returns a row (an aggregate without <c>GROUP BY</c>, a query without <c>FROM</c>, a
    /// <c>VALUES</c> list) never gives it.
    /// </summary>
    public const string OuterApply = "outer-apply";

    /// <summary>It is a subquery, which is null when it returns no rows.</summary>
    public const string EmptySubquery = "empty-subquery";

    /// <summary>It is a nested object that is left out when every one of its fields is null.</summary>
    public const string AllFieldsNull = "all-fields-null";

    /// <summary>It is an aggregate (<c>MIN</c>, <c>MAX</c>, <c>json_arrayagg</c>, <c>json_objectagg</c>) without GROUP BY, which is null over no rows.</summary>
    public const string EmptyAggregate = "empty-aggregate";

    /// <summary>It is a scalar function's result, typed from the declared return type only, so it may be NULL.</summary>
    public const string FunctionResult = "function-result";

    /// <summary>It is a function's parameter, which a caller may pass as NULL.</summary>
    public const string Parameter = "parameter";

    /// <summary>
    /// It is a call of a function declared <c>WITH RETURNS NULL ON NULL INPUT</c> with an argument that may be null: such
    /// a call is NULL without running the body.
    /// </summary>
    public const string NullInput = "null-input";

    /// <summary>It is a query result, which returns nothing when no rows match.</summary>
    public const string NoRows = "no-rows";

    /// <summary>
    /// It is a result whose statement may not run (it is in a branch, or after a <c>RETURN</c> that may be taken), so the
    /// result set may be missing altogether rather than empty. Only a result after which every result may be missing too
    /// has it, so the result sets a client receives are always the first ones listed.
    /// </summary>
    public const string NotRun = "not-run";

    /// <summary>It is a procedure's variable, which holds null until a value that is never null is assigned on every path.</summary>
    public const string Variable = "variable";

    /// <summary>
    /// It is a <c>COUNT</c>, an <c>int</c>, which over more than 2,147,483,647 rows overflows: an error by default, but NULL
    /// when the caller's session has <c>ANSI_WARNINGS</c> and <c>ARITHABORT</c> off, which a procedure cannot prevent.
    /// <c>COUNT_BIG</c> does not.
    /// </summary>
    public const string CountOverflow = "count-overflow";

    /// <summary>
    /// It is arithmetic, a <c>SUM</c> or <c>AVG</c>, a numeric function (<c>ROUND</c>, <c>ABS</c>, <c>POWER</c>, <c>EXP</c>, …) or a
    /// conversion that can overflow its type or divide by zero: an error by default, but NULL when the caller's session has
    /// <c>ANSI_WARNINGS</c> and <c>ARITHABORT</c> off, which a procedure cannot prevent.
    /// <c>COALESCE</c> or <c>ISNULL</c> with a value never null removes it.
    /// </summary>
    public const string ArithmeticOverflow = "arithmetic-overflow";

    /// <summary>It is <c>TRY_CAST</c> or <c>TRY_CONVERT</c> of a value that may not convert, which gives NULL instead of an error.</summary>
    public const string FailedConversion = "failed-conversion";

    /// <summary>
    /// It is a grouped column of a query grouped by <c>ROLLUP</c>, <c>CUBE</c> or <c>GROUPING SETS</c>, whose super-aggregate
    /// rows hold NULL in the columns they roll up, whatever the filters proved of them.
    /// </summary>
    public const string GroupingSet = "grouping-set";

    /// <summary>
    /// It is a column of a <c>MERGE</c>'s <c>OUTPUT</c> that has no value for some of the actions the statement takes: a
    /// <c>deleted</c> column for the rows it inserts, an <c>inserted</c> column for the rows it deletes, and a column of its
    /// source for the rows it changes because they are not matched by the source.
    /// </summary>
    public const string MergeAction = "merge-action";

    /// <summary>It is a <c>CASE</c> without <c>ELSE</c>, which is NULL when no <c>WHEN</c> holds.</summary>
    public const string NoElse = "no-else";

    /// <summary>
    /// A result of a <c>CASE</c>, <c>IIF</c> or <c>CHOOSE</c> it may give is the <c>NULL</c> literal, or it is (or reads) the
    /// <c>NULL</c> literal itself, as a bare <c>NULL</c> select item (an <c>int</c>) or <c>CAST(NULL AS date)</c> (0.14).
    /// </summary>
    public const string NullLiteral = "null-literal";

    /// <summary>It is a <c>CHOOSE</c> whose index may be below 1 or above its count of values, which gives NULL.</summary>
    public const string OutOfRange = "out-of-range";

    /// <summary>
    /// It is a <c>COALESCE</c> one of whose arguments, a subquery or a call, is tested and then read again: the second read may
    /// be NULL (another statement changed the rows between them, or the call gives another value). Before 0.12 the argument's
    /// own reasons were given instead. No seed reaches it; it is observed.
    /// </summary>
    public const string ReadTwice = "read-twice";

    /// <summary>It is a <c>NULLIF</c>, NULL when its two arguments compare equal.</summary>
    public const string NullIf = "nullif";

    /// <summary>
    /// It is a column <c>OPENJSON</c> reads from JSON (0.13), or a <c>JSON_VALUE</c> (0.14): NULL where the JSON holds
    /// <c>null</c> there, under a <c>strict</c> path too, and, under a lax path, where nothing is there, or an <c>AS JSON</c>
    /// column finds a scalar, or a scalar column or <c>JSON_VALUE</c> an object or array (or, for <c>JSON_VALUE</c>, a string
    /// longer than 4,000 characters). (A number too large for the column is NULL as well under the caller's
    /// <c>ANSI_WARNINGS</c> and <c>ARITHABORT</c> off.)
    /// </summary>
    public const string JsonValue = "json-value";

    /// <summary>
    /// It is a window function over a frame that may hold no row (0.14): an aggregate, <c>FIRST_VALUE</c> or
    /// <c>LAST_VALUE</c> whose <c>ROWS</c> frame may leave out the current row (<c>ROWS BETWEEN 2 PRECEDING AND 1 PRECEDING</c>
    /// has nothing before the first row) is NULL there, over a column never null too. The default frame, and any frame that
    /// holds the current row, is never empty.
    /// </summary>
    public const string EmptyFrame = "empty-frame";

    /// <summary>
    /// It is <c>LAG</c> or <c>LEAD</c> without a default (0.14), NULL where the row it reads is past the partition's edge.
    /// </summary>
    public const string WindowEdge = "window-edge";

    /// <summary>
    /// It is a math function that may be given a value outside its domain (0.15): <c>SQRT</c> of a negative, <c>LOG</c> or
    /// <c>LOG10</c> of 0 or less or to a base of 0 or 1, <c>POWER</c> of a negative to a fractional power. That is an invalid
    /// floating point operation: an error by default, but NULL when the caller's session has <c>ANSI_WARNINGS</c> and
    /// <c>ARITHABORT</c> off, which a procedure cannot prevent, as for <see cref="ArithmeticOverflow"/>.
    /// </summary>
    public const string DomainError = "domain-error";

    /// <summary>
    /// It is a sample statistic, <c>STDEV</c> or <c>VAR</c> (0.15), which is NULL over fewer than two values that are not NULL:
    /// a group or a window's frame of one row, with <c>GROUP BY</c> too. The population forms, <c>STDEVP</c> and <c>VARP</c>,
    /// are 0 over one value.
    /// </summary>
    public const string TooFewValues = "too-few-values";

    /// <summary>Its type is unknown, so nothing is proved about it.</summary>
    public const string Unknown = "unknown";

    /// <summary>
    /// The config file declares it may be null, or absent: in results declared for a procedure whose results cannot be
    /// inferred. <see cref="IrNullReason.At"/> is the procedure declared.
    /// </summary>
    public const string Declared = "declared";
}
