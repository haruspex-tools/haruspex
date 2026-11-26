namespace Haruspex.Ir;

/// <summary>
/// What each of the IR's own codes means, in words, for the reader of generated code and of <c>haruspex explain</c>: the null
/// rules (<see cref="IrNullRule"/>), the unknown reasons (<see cref="IrUnknownReason"/>) and the cardinalities
/// (<see cref="IrCardinality"/>). A code is its name in the IR's JSON (<see cref="IrWireNames"/>), so a tool learns one vocabulary
/// for the file and the explanation; each has a page under <c>docs/reasons/</c> that says more and names the test it rests on
/// (<c>docs/decisions/0079-a-surprise-is-explained-where-it-is-written.md</c>). <c>ReasonCatalogueTests</c> fails for a code
/// without its text, its page or its test.
/// </summary>
/// <remarks>
/// Each text finishes the sentence "may be null because …" or "is unknown because …", in lower case and without a full stop, so
/// that an emitter can join several. Like the enums' own documentation, a text names SQL Server's construct as the example where
/// a reader needs one to act (<c>COUNT_BIG</c>): the codes are neutral, and their words are for people.
/// </remarks>
public static class IrReasons
{
    /// <summary>Where the pages are published, as the diagnostics' pages are: <see cref="HaruspexSite.Reasons"/>.</summary>
    public const string Pages = HaruspexSite.Reasons;

    /// <summary>The page of a code, <c>https://haruspex.tools/reasons/count-overflow</c>, which leads to <c>docs/reasons/count-overflow.md</c> on <c>main</c>.</summary>
    public static string Page(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return Pages + code;
    }

    /// <summary>Why a value may be null, for "may be null because …".</summary>
    public static string Text(IrNullRule rule) => rule switch
    {
        IrNullRule.NullableColumn => "the column it reads is declared nullable",
        IrNullRule.UnmatchedRow => "it comes from the optional side of an outer join, which has no row when nothing matches",
        IrNullRule.UnmatchedLateralRow => "it comes from an OUTER APPLY whose query may return no row",
        IrNullRule.EmptySubquery => "it is a subquery, which is null when it returns no rows",
        IrNullRule.AllFieldsNull => "it is a nested object left out when every one of its values is null",
        IrNullRule.EmptyAggregate => "it is an aggregate without GROUP BY, which is null over no rows",
        IrNullRule.FunctionResult => "it is a function's result, typed from its declared return type, which may be null",
        IrNullRule.Parameter => "it is a parameter, which a caller may pass as null",
        IrNullRule.NullInput => "it calls a function that returns null on null input with an argument that may be null",
        IrNullRule.NoRows => "the query returns nothing when no rows match",
        IrNullRule.NotRun => "the statement that returns it may not run",
        IrNullRule.Variable => "it is a variable that may not have been given a value that is never null",
        IrNullRule.CountOverflow => "COUNT is an int, null past 2,147,483,647 rows when the caller's session has ANSI_WARNINGS and ARITHABORT off",
        IrNullRule.ArithmeticOverflow => "arithmetic that overflows or divides by zero is null when the caller's session has ANSI_WARNINGS and ARITHABORT off",
        IrNullRule.PrecisionLoss => "a decimal rounded to fewer places is null under a caller's NUMERIC_ROUNDABORT on with ANSI_WARNINGS and ARITHABORT off",
        IrNullRule.FailedConversion => "TRY_CAST and TRY_CONVERT give null for a value that doesn't convert",
        IrNullRule.SubtotalRow => "ROLLUP, CUBE or GROUPING SETS add subtotal rows, which hold null in it",
        IrNullRule.MissingForAction => "a MERGE's OUTPUT has no value for it for some of its actions",
        IrNullRule.NoBranchMatched => "a CASE without ELSE is null when no condition holds",
        IrNullRule.NullLiteral => "it is, or may be, the NULL literal",
        IrNullRule.OutOfRange => "a function given an index, code or position outside what it reads gives null",
        IrNullRule.ReadTwice => "COALESCE reads its argument a second time, which may then be null",
        IrNullRule.NullWhenEqual => "NULLIF is null when its two arguments are equal",
        IrNullRule.JsonNullOrAbsent => "it is read from JSON, which may hold null or nothing there",
        IrNullRule.EmptyFrame => "a window function over a frame that may hold no row is null there",
        IrNullRule.WindowEdge => "LAG or LEAD without a default is null past the partition's edge",
        IrNullRule.DomainError => "a math function outside its domain is null when the caller's session has ANSI_WARNINGS and ARITHABORT off",
        IrNullRule.TooFewValues => "STDEV and VAR are null over fewer than two values",
        IrNullRule.Unknown => "its type is unknown, so nothing is proved of it",
        IrNullRule.Declared => "the config file declares it may be",
        IrNullRule.NotInBoth => "only one of the two databases returns it",
        IrNullRule.XmlValue => "XML's value(), or an OPENXML column, is null where its path finds no node, or the XML is null",
        _ => throw new ArgumentOutOfRangeException(nameof(rule), rule, null),
    };

    /// <summary>What a team can change to remove the reason, when there is something; null when the reason is the data's own.</summary>
    public static string? WayOut(IrNullRule rule) => rule switch
    {
        IrNullRule.CountOverflow => "use COUNT_BIG, or declare the callers' ANSI_WARNINGS or ARITHABORT on in the config file's \"session\"",
        IrNullRule.ArithmeticOverflow or IrNullRule.DomainError => "declare the callers' ANSI_WARNINGS or ARITHABORT on in the config file's \"session\", or wrap it in COALESCE or ISNULL",
        IrNullRule.PrecisionLoss => "declare the callers' NUMERIC_ROUNDABORT off in the config file's \"session\"",
        IrNullRule.NoBranchMatched => "give the CASE an ELSE",
        IrNullRule.WindowEdge => "give LAG or LEAD a default",
        IrNullRule.UnmatchedRow or IrNullRule.UnmatchedLateralRow or IrNullRule.EmptySubquery or IrNullRule.EmptyAggregate
            or IrNullRule.NullableColumn or IrNullRule.FunctionResult or IrNullRule.Variable =>
            "if the data rules it out, declare it in the config file with \"nullable\": false and why",
        _ => null,
    };

    /// <summary>Why a value or result is unknown, for "is unknown because …".</summary>
    public static string Text(IrUnknownReason reason) => reason switch
    {
        IrUnknownReason.UnsupportedStatement => "a statement it runs is not modelled yet",
        IrUnknownReason.ExternalCode => "it is made by code outside the project, such as a CLR procedure",
        IrUnknownReason.Redefined => "the object is defined more than once, so which definition applies depends on order",
        IrUnknownReason.UnsupportedQuery => "a query form is not modelled yet",
        IrUnknownReason.UnsupportedExpression => "an expression or function is not modelled yet",
        IrUnknownReason.UnsupportedSource => "a table source or join is not modelled yet",
        IrUnknownReason.UnsupportedSqlType => "a SQL type is not modelled yet",
        IrUnknownReason.UnresolvedTable => "a table it reads is not defined in the project",
        IrUnknownReason.UnresolvedColumn => "a column can't be found, or is ambiguous, in the tables in scope",
        IrUnknownReason.UnnamedColumn => "a result column has no name",
        IrUnknownReason.DuplicateColumn => "two result columns claim the same key",
        IrUnknownReason.UnresolvedRecursion => "functions that call themselves reached no fixed point",
        IrUnknownReason.JsonShapeUnknown => "the JSON's shape is in the data, not the code",
        IrUnknownReason.CallerTempTable => "it reads a temporary table its caller may create, whose shape depends on who calls",
        IrUnknownReason.VaryingResults => "which result sets it returns depends on the path in a way a list can't say",
        IrUnknownReason.DynamicSql => "it runs dynamic SQL, a string built when it runs",
        IrUnknownReason.ProcedureVariable => "the procedure it calls is named in a variable",
        IrUnknownReason.SystemProcedure => "it calls a system procedure, whose results are not modelled",
        IrUnknownReason.MissingProcedure => "it calls a procedure the project doesn't define",
        IrUnknownReason.AmbiguousProcedure => "the procedure it calls is named without a schema that decides which",
        IrUnknownReason.TriggerResults => "a trigger it fires may return results of its own",
        IrUnknownReason.HiddenColumn => "the result has a column the client receives but can't name",
        IrUnknownReason.EncryptedDefinition => "the object is encrypted and its text is not in the source",
        IrUnknownReason.SqlcmdVariable => "it holds a SQLCMD variable, which a deployment replaces with its value",
        IrUnknownReason.UndecidedParameter => "nothing in the query decides the parameter's type",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };

    /// <summary>What a team can do about an unknown: declare it, or change the SQL.</summary>
    public static string WayOut(IrUnknownReason reason) => reason switch
    {
        IrUnknownReason.UndecidedParameter => "declare the parameter's type in the query file (-- @param)",
        IrUnknownReason.UnnamedColumn => "give the column a name",
        IrUnknownReason.DuplicateColumn => "give each column a name of its own",
        IrUnknownReason.JsonShapeUnknown => "type it in the config file with \"type\" and \"import\"",
        _ => "declare the results in the config file's \"results\"",
    };

    /// <summary>What a result's cardinality means, for "returns …".</summary>
    public static string Text(IrCardinality cardinality) => cardinality switch
    {
        IrCardinality.Many => "any number of rows, none included: nothing bounds them",
        IrCardinality.AtMostOne => "at most one row: TOP (1), FETCH NEXT 1, or a filter that fixes a key",
        IrCardinality.One => "exactly one row: an aggregate without GROUP BY, or a query without FROM",
        _ => throw new ArgumentOutOfRangeException(nameof(cardinality), cardinality, null),
    };
}
