using System.Collections.Frozen;

namespace Haruspex.Ir;

/// <summary>
/// The name each value of each closed vocabulary has in the IR's JSON: one table per vocabulary, which the writer uses and the reader
/// reads backwards (<c>TryParse</c>), so that each name is written in one place. The names are the format's, and never change within a version.
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
        IrScalarKind.Bytes => "bytes",
        IrScalarKind.Xml => "xml",
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
        IrObjectKind.Query => "query",
    };

    /// <summary>The kind of object a named type belongs to (the key in <c>identity</c>).</summary>
    public static string Of(IrTypeOwner value) => value switch
    {
        IrTypeOwner.Procedure => "procedure",
        IrTypeOwner.Function => "function",
        IrTypeOwner.Query => "query",
        IrTypeOwner.TableType => "tableType",
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
        IrPresence.Optional => "optional",
    };

    /// <summary>Why a value can be null (<c>rule</c>).</summary>
    public static string Of(IrNullRule value) => value switch
    {
        IrNullRule.NullableColumn => "nullable-column",
        IrNullRule.UnmatchedRow => "unmatched-row",
        IrNullRule.UnmatchedLateralRow => "unmatched-lateral-row",
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
        IrNullRule.SubtotalRow => "subtotal-row",
        IrNullRule.MissingForAction => "missing-for-action",
        IrNullRule.NoBranchMatched => "no-branch-matched",
        IrNullRule.NullLiteral => "null-literal",
        IrNullRule.OutOfRange => "out-of-range",
        IrNullRule.ReadTwice => "read-twice",
        IrNullRule.NullWhenEqual => "null-when-equal",
        IrNullRule.JsonNullOrAbsent => "json-null-or-absent",
        IrNullRule.EmptyFrame => "empty-frame",
        IrNullRule.WindowEdge => "window-edge",
        IrNullRule.DomainError => "domain-error",
        IrNullRule.PrecisionLoss => "precision-loss",
        IrNullRule.TooFewValues => "too-few-values",
        IrNullRule.Unknown => "unknown",
        IrNullRule.Declared => "declared",
        IrNullRule.NotInBoth => "not-in-both",
        IrNullRule.XmlValue => "xml-value",
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
        IrUnknownReason.EncryptedDefinition => "encrypted-definition",
        IrUnknownReason.SqlcmdVariable => "sqlcmd-variable",
        IrUnknownReason.UndecidedParameter => "undecided-parameter",
    };

    /// <summary>What an override sets (<c>setting</c>).</summary>
    public static string Of(IrOverrideSetting value) => value switch
    {
        IrOverrideSetting.NotNull => "not-null",
        IrOverrideSetting.Type => "type",
        IrOverrideSetting.ExactValues => "exact-values",
        IrOverrideSetting.Results => "results",
        IrOverrideSetting.ColumnJson => "column-json",
        IrOverrideSetting.EndpointJson => "endpoint-json",
    };

    /// <summary>Where a fact came from (the key in <c>from</c>).</summary>
    public static string Of(IrFactSourceKind value) => value switch
    {
        IrFactSourceKind.Type => "type",
        IrFactSourceKind.Check => "check",
        IrFactSourceKind.Engine => "engine",
        IrFactSourceKind.Expression => "expression",
        IrFactSourceKind.Declared => "declared",
        IrFactSourceKind.XmlSchema => "xml-schema",
    };

    /// <summary>What a length counts (<c>unit</c>).</summary>
    public static string Of(IrLengthUnit value) => value switch
    {
        IrLengthUnit.Utf16 => "utf16",
        IrLengthUnit.Bytes => "bytes",
        IrLengthUnit.CodePoints => "code-points",
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
        IrValueMatch.Canonical => "canonical",
    };

    /// <summary>How the server fills a table type's column (<c>generated</c>, 0.26).</summary>
    public static string Of(IrColumnGeneration value) => value switch
    {
        IrColumnGeneration.Identity => "identity",
        IrColumnGeneration.Computed => "computed",
        IrColumnGeneration.Rowversion => "rowversion",
    };

    /// <summary>The member of a scalar's kind that <paramref name="name"/> names, or false when it names none.</summary>
    public static bool TryParse(string name, out IrScalarKind value) => ScalarKinds.TryGetValue(name, out value);

    /// <summary>The member of a scalar's format that <paramref name="name"/> names, or false when it names none.</summary>
    public static bool TryParse(string name, out IrFormat value) => Formats.TryGetValue(name, out value);

    /// <summary>The member of an object's kind that <paramref name="name"/> names, or false when it names none.</summary>
    public static bool TryParse(string name, out IrObjectKind value) => ObjectKinds.TryGetValue(name, out value);

    /// <summary>The member of the kind of object a named type belongs to that <paramref name="name"/> names, or false when it names none.</summary>
    public static bool TryParse(string name, out IrTypeOwner value) => TypeOwners.TryGetValue(name, out value);

    /// <summary>The member of how a result set arrives that <paramref name="name"/> names, or false when it names none.</summary>
    public static bool TryParse(string name, out IrEncoding value) => Encodings.TryGetValue(name, out value);

    /// <summary>The member of how many rows a result set has that <paramref name="name"/> names, or false when it names none.</summary>
    public static bool TryParse(string name, out IrCardinality value) => Cardinalities.TryGetValue(name, out value);

    /// <summary>The member of whether a field is always there that <paramref name="name"/> names, or false when it names none.</summary>
    public static bool TryParse(string name, out IrPresence value) => Presences.TryGetValue(name, out value);

    /// <summary>The member of why a value can be null that <paramref name="name"/> names, or false when it names none.</summary>
    public static bool TryParse(string name, out IrNullRule value) => NullRules.TryGetValue(name, out value);

    /// <summary>The member of why a value or a result is unknown that <paramref name="name"/> names, or false when it names none.</summary>
    public static bool TryParse(string name, out IrUnknownReason value) => UnknownReasons.TryGetValue(name, out value);

    /// <summary>The member of what an override sets that <paramref name="name"/> names, or false when it names none.</summary>
    public static bool TryParse(string name, out IrOverrideSetting value) => OverrideSettings.TryGetValue(name, out value);

    /// <summary>The member of where a fact came from that <paramref name="name"/> names, or false when it names none.</summary>
    public static bool TryParse(string name, out IrFactSourceKind value) => FactSourceKinds.TryGetValue(name, out value);

    /// <summary>The member of what a length counts that <paramref name="name"/> names, or false when it names none.</summary>
    public static bool TryParse(string name, out IrLengthUnit value) => LengthUnits.TryGetValue(name, out value);

    /// <summary>The member of how a length in bytes encodes its text that <paramref name="name"/> names, or false when it names none.</summary>
    public static bool TryParse(string name, out IrByteEncoding value) => ByteEncodings.TryGetValue(name, out value);

    /// <summary>The member of how a value set's items are compared that <paramref name="name"/> names, or false when it names none.</summary>
    public static bool TryParse(string name, out IrValueMatch value) => ValueMatches.TryGetValue(name, out value);

    /// <summary>The member of how a table type's column is filled that <paramref name="name"/> names, or false when it names none.</summary>
    public static bool TryParse(string name, out IrColumnGeneration value) => ColumnGenerations.TryGetValue(name, out value);

    private static readonly FrozenDictionary<string, IrColumnGeneration> ColumnGenerations = Inverse<IrColumnGeneration>(Of);

    private static readonly FrozenDictionary<string, IrScalarKind> ScalarKinds = Inverse<IrScalarKind>(Of);

    private static readonly FrozenDictionary<string, IrFormat> Formats = Inverse<IrFormat>(Of);

    private static readonly FrozenDictionary<string, IrObjectKind> ObjectKinds = Inverse<IrObjectKind>(Of);

    private static readonly FrozenDictionary<string, IrTypeOwner> TypeOwners = Inverse<IrTypeOwner>(Of);

    private static readonly FrozenDictionary<string, IrEncoding> Encodings = Inverse<IrEncoding>(Of);

    private static readonly FrozenDictionary<string, IrCardinality> Cardinalities = Inverse<IrCardinality>(Of);

    private static readonly FrozenDictionary<string, IrPresence> Presences = Inverse<IrPresence>(Of);

    private static readonly FrozenDictionary<string, IrNullRule> NullRules = Inverse<IrNullRule>(Of);

    private static readonly FrozenDictionary<string, IrUnknownReason> UnknownReasons = Inverse<IrUnknownReason>(Of);

    private static readonly FrozenDictionary<string, IrOverrideSetting> OverrideSettings = Inverse<IrOverrideSetting>(Of);

    private static readonly FrozenDictionary<string, IrFactSourceKind> FactSourceKinds = Inverse<IrFactSourceKind>(Of);

    private static readonly FrozenDictionary<string, IrLengthUnit> LengthUnits = Inverse<IrLengthUnit>(Of);

    private static readonly FrozenDictionary<string, IrByteEncoding> ByteEncodings = Inverse(Of, IrByteEncoding.Unknown);

    private static readonly FrozenDictionary<string, IrValueMatch> ValueMatches = Inverse<IrValueMatch>(Of);

    /// <summary>
    /// A vocabulary's table read backwards, built from <c>Of</c> so that the two directions can't disagree. A member left out
    /// of the JSON (<paramref name="unnamed"/>) has no name to read.
    /// </summary>
    private static FrozenDictionary<string, T> Inverse<T>(Func<T, string> of, params T[] unnamed)
        where T : struct, Enum =>
        Enum.GetValues<T>().Where(v => !unnamed.Contains(v)).ToFrozenDictionary(of, StringComparer.Ordinal);
}
