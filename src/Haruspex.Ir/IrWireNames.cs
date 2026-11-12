namespace Haruspex.Ir;

/// <summary>
/// The name each value of each closed vocabulary has in the IR's JSON: one table per vocabulary, which the writer uses, so that
/// each name is written in one place. The names are the format's, and never change within a version.
/// </summary>
/// <remarks>
/// Each table is a switch with a case for every member and no default, so a member added without a name fails the build
/// (CS8509). A value that is no member (an integer cast to the enum) throws. <c>IrWireNamesTests</c> holds every table to the
/// names the format has, and checks that no two members of a vocabulary share one.
/// </remarks>
public static class IrWireNames
{
    /// <summary>A scalar's kind (<c>kind</c>).</summary>
    public static string Of(IrScalarKind value) => value switch
    {
        IrScalarKind.String => "string",
        IrScalarKind.Number => "number",
        IrScalarKind.Boolean => "boolean",
        IrScalarKind.Date => "date",
        IrScalarKind.Time => "time",
        IrScalarKind.Datetime => "datetime",
        IrScalarKind.Uuid => "uuid",
    };

    /// <summary>A scalar's format (<c>format</c>).</summary>
    public static string Of(IrFormat value) => value switch
    {
        IrFormat.Uint8 => "uint8",
        IrFormat.Int16 => "int16",
        IrFormat.Int32 => "int32",
        IrFormat.Int64 => "int64",
        IrFormat.Decimal => "decimal",
        IrFormat.Float => "float",
        IrFormat.Local => "local",
        IrFormat.Offset => "offset",
    };

    /// <summary>An object's kind (<c>kind</c>).</summary>
    public static string Of(IrObjectKind value) => value switch
    {
        IrObjectKind.Procedure => "procedure",
    };

    /// <summary>The kind of object a named type belongs to (the key in <c>identity</c>).</summary>
    public static string Of(IrTypeOwner value) => value switch
    {
        IrTypeOwner.Procedure => "procedure",
        IrTypeOwner.Function => "function",
    };

    /// <summary>How a result set arrives (<c>encoding</c>).</summary>
    public static string Of(IrEncoding value) => value switch
    {
        IrEncoding.Rowset => "rowset",
        IrEncoding.Json => "json",
    };

    /// <summary>How many rows a result set has (<c>cardinality</c>).</summary>
    public static string Of(IrCardinality value) => value switch
    {
        IrCardinality.Many => "many",
        IrCardinality.AtMostOne => "atMostOne",
        IrCardinality.One => "one",
    };

    /// <summary>Whether a field is always there (<c>presence</c>).</summary>
    public static string Of(IrPresence value) => value switch
    {
        IrPresence.Always => "always",
        IrPresence.OmittedWhenNull => "omittedWhenNull",
    };

    /// <summary>Why a value can be null (<c>rule</c>).</summary>
    public static string Of(IrNullRule value) => value switch
    {
        IrNullRule.NullableColumn => "nullable-column",
        IrNullRule.OuterJoin => "outer-join",
        IrNullRule.OuterApply => "outer-apply",
        IrNullRule.EmptySubquery => "empty-subquery",
        IrNullRule.AllFieldsNull => "all-fields-null",
        IrNullRule.EmptyAggregate => "empty-aggregate",
        IrNullRule.FunctionResult => "function-result",
        IrNullRule.Parameter => "parameter",
        IrNullRule.NullInput => "null-input",
        IrNullRule.NoRows => "no-rows",
        IrNullRule.NotRun => "not-run",
        IrNullRule.Variable => "variable",
        IrNullRule.CountOverflow => "count-overflow",
        IrNullRule.ArithmeticOverflow => "arithmetic-overflow",
        IrNullRule.FailedConversion => "failed-conversion",
        IrNullRule.GroupingSet => "grouping-set",
        IrNullRule.MergeAction => "merge-action",
        IrNullRule.NoElse => "no-else",
        IrNullRule.NullLiteral => "null-literal",
        IrNullRule.OutOfRange => "out-of-range",
        IrNullRule.ReadTwice => "read-twice",
        IrNullRule.NullIf => "nullif",
        IrNullRule.JsonValue => "json-value",
        IrNullRule.EmptyFrame => "empty-frame",
        IrNullRule.WindowEdge => "window-edge",
        IrNullRule.DomainError => "domain-error",
        IrNullRule.TooFewValues => "too-few-values",
        IrNullRule.Unknown => "unknown",
        IrNullRule.Declared => "declared",
    };

    /// <summary>Why a value or a result is unknown (<c>reason</c>).</summary>
    public static string Of(IrUnknownReason value) => value switch
    {
        IrUnknownReason.UnsupportedStatement => "unsupported-statement",
        IrUnknownReason.ExternalCode => "external-code",
        IrUnknownReason.Redefined => "redefined",
        IrUnknownReason.UnsupportedQuery => "unsupported-query",
        IrUnknownReason.UnsupportedExpression => "unsupported-expression",
        IrUnknownReason.UnsupportedSource => "unsupported-source",
        IrUnknownReason.UnsupportedSqlType => "unsupported-sql-type",
        IrUnknownReason.UnresolvedTable => "unresolved-table",
        IrUnknownReason.UnresolvedColumn => "unresolved-column",
        IrUnknownReason.UnnamedColumn => "unnamed-column",
        IrUnknownReason.DuplicateColumn => "duplicate-column",
        IrUnknownReason.UnresolvedRecursion => "unresolved-recursion",
        IrUnknownReason.JsonShapeUnknown => "json-shape-unknown",
        IrUnknownReason.CallerTempTable => "caller-temp-table",
        IrUnknownReason.VaryingResults => "varying-results",
        IrUnknownReason.DynamicSql => "dynamic-sql",
        IrUnknownReason.ProcedureVariable => "procedure-variable",
        IrUnknownReason.SystemProcedure => "system-procedure",
        IrUnknownReason.MissingProcedure => "missing-procedure",
        IrUnknownReason.AmbiguousProcedure => "ambiguous-procedure",
        IrUnknownReason.TriggerResults => "trigger-results",
        IrUnknownReason.HiddenColumn => "hidden-column",
    };

    /// <summary>What an override sets (<c>setting</c>).</summary>
    public static string Of(IrOverrideSetting value) => value switch
    {
        IrOverrideSetting.NotNull => "not-null",
        IrOverrideSetting.Type => "type",
        IrOverrideSetting.ExactValues => "exact-values",
        IrOverrideSetting.Results => "results",
    };

    /// <summary>Where a fact came from (the key in <c>from</c>).</summary>
    public static string Of(IrFactSourceKind value) => value switch
    {
        IrFactSourceKind.Type => "type",
        IrFactSourceKind.Check => "check",
        IrFactSourceKind.Engine => "engine",
        IrFactSourceKind.Expression => "expression",
    };

    /// <summary>What a length counts (<c>unit</c>).</summary>
    public static string Of(IrLengthUnit value) => value switch
    {
        IrLengthUnit.Utf16 => "utf16",
        IrLengthUnit.Bytes => "bytes",
    };

    /// <summary>How a length in bytes encodes its text (<c>encoding</c>).</summary>
    public static string Of(IrByteEncoding value) => value switch
    {
        IrByteEncoding.Unknown => throw new ArgumentOutOfRangeException(nameof(value), value, "An unknown encoding has no name: the writer leaves it out."),
        IrByteEncoding.SingleByte => "single-byte",
        IrByteEncoding.Utf8 => "utf-8",
        IrByteEncoding.DoubleByte => "double-byte",
    };

    /// <summary>How a value set's items are compared (<c>match</c>).</summary>
    public static string Of(IrValueMatch value) => value switch
    {
        IrValueMatch.Exact => "exact",
        IrValueMatch.Collation => "collation",
        IrValueMatch.Padded => "padded",
    };
}
