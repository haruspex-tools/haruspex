using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;
using Haruspex.SqlServer.Semantics.Collations;

namespace Haruspex.SqlServer.Inference;

/// <summary>Maps SQL Server types to the IR's logical types, keeping the SQL type as written.</summary>
/// <remarks>
/// The model says what a type is (<see cref="SqlType"/>), and Semantics what it and the column's constraints
/// guarantee (<see cref="ColumnFacts"/>); what they become in the IR is inference's choice, so the mapping lives here.
/// </remarks>
internal static class SqlTypes
{
    /// <summary>A function's declared return type: its kind, with an alias resolved, but no facets.</summary>
    public static Shape ToShape(DatabaseModel model, SqlType? type, SourceLocation at) =>
        SchemaRules.ResolveType(model, type) is { } resolved
            ? ToShape(new ColumnFacts(resolved), resolved, at, withFacets: false)
            : Unsupported(type, at);

    /// <summary>
    /// A value read inside a body after SQL Server converted it to <paramref name="type"/>, such as a function's parameter:
    /// the type's facts hold, because the conversion cuts a string short and rounds a decimal (observed; an overflow is an
    /// error, not a value). No collation is known, so a <c>varchar</c>'s byte length has no encoding.
    /// </summary>
    public static Shape ToConvertedShape(DatabaseModel model, SqlType? type, SourceLocation at) =>
        SchemaRules.ResolveType(model, type) is { } resolved
            ? ToShape(SchemaRules.TypeFacts(resolved, collation: null), resolved, at, withFacets: true)
            : Unsupported(type, at);

    /// <summary>
    /// A procedure's parameter: its declared type, with that type's facts, in the database's default collation, which a parameter
    /// takes as a column without <c>COLLATE</c> does.
    /// </summary>
    /// <remarks>
    /// The facts are what SQL Server keeps of a value sent to the parameter: a string longer than <c>nvarchar(20)</c> is cut
    /// short and a decimal of more places is rounded, without an error (<c>ParameterObservationTests</c>). So they are true of the
    /// value inside the body, and they are the rule a client checks a value against before sending it, which the generated
    /// database module states (<c>docs/decisions/0054-a-parameter-is-checked-against-its-declared-type.md</c>). A guard in the
    /// body proves nothing about what a caller may send, so nothing narrower is claimed here.
    /// </remarks>
    public static Shape ToParameterShape(DatabaseModel model, SqlType? type, SourceLocation at) =>
        SchemaRules.ResolveType(model, type) is { } resolved
            ? ToShape(SchemaRules.TypeFacts(resolved, model.DefaultCollation), resolved, at, withFacets: true)
            : Unsupported(type, at);

    /// <summary>A column's type, with every fact Semantics proved about its values.</summary>
    public static Shape ToShape(ColumnFacts facts, SqlType? declared, SourceLocation at) =>
        facts.Type is { } resolved ? ToShape(facts, resolved, at, withFacets: true) : Unsupported(declared, at);

    /// <summary>A value of a type the server's rules computed: the type's facts, with no collation known.</summary>
    public static Shape ToConvertedShape(ScalarType type, SourceLocation at)
    {
        var resolved = ResolvedType.Of(type);
        return ToShape(SchemaRules.TypeFacts(resolved, collation: null), resolved, at, withFacets: true);
    }

    /// <summary>
    /// The built-in types inference gives a value of, and what each is in the IR. A type not listed reaches the IR as unknown
    /// (<see cref="IrUnknownReason.UnsupportedSqlType"/>); <c>json</c> is text (<see cref="JsonTextShape"/>), not a scalar.
    /// </summary>
    private static readonly Dictionary<BuiltInType, (IrScalarKind Kind, IrFormat? Format)> Scalars = new()
    {
        [BuiltInType.TinyInt] = (IrScalarKind.Number, IrFormat.Uint8),
        [BuiltInType.SmallInt] = (IrScalarKind.Number, IrFormat.Int16),
        [BuiltInType.Int] = (IrScalarKind.Number, IrFormat.Int32),
        [BuiltInType.BigInt] = (IrScalarKind.Number, IrFormat.Int64),
        [BuiltInType.Decimal] = (IrScalarKind.Number, IrFormat.Decimal),
        [BuiltInType.Numeric] = (IrScalarKind.Number, IrFormat.Decimal),
        [BuiltInType.Money] = (IrScalarKind.Number, IrFormat.Decimal),
        [BuiltInType.SmallMoney] = (IrScalarKind.Number, IrFormat.Decimal),
        [BuiltInType.Float] = (IrScalarKind.Number, IrFormat.Float),
        [BuiltInType.Real] = (IrScalarKind.Number, IrFormat.Float),
        [BuiltInType.Bit] = (IrScalarKind.Boolean, null),
        [BuiltInType.Char] = (IrScalarKind.String, null),
        [BuiltInType.VarChar] = (IrScalarKind.String, null),
        [BuiltInType.NChar] = (IrScalarKind.String, null),
        [BuiltInType.NVarChar] = (IrScalarKind.String, null),
        [BuiltInType.Date] = (IrScalarKind.Date, null),
        [BuiltInType.Time] = (IrScalarKind.Time, null),
        [BuiltInType.DateTime] = (IrScalarKind.Datetime, IrFormat.Local),
        [BuiltInType.DateTime2] = (IrScalarKind.Datetime, IrFormat.Local),
        [BuiltInType.SmallDateTime] = (IrScalarKind.Datetime, IrFormat.Local),
        [BuiltInType.DateTimeOffset] = (IrScalarKind.Datetime, IrFormat.Offset),
        [BuiltInType.UniqueIdentifier] = (IrScalarKind.Uuid, null),
    };

    /// <summary>Whether inference gives a value of this type as a scalar.</summary>
    public static bool IsModelled(BuiltInType type) => Scalars.ContainsKey(type);

    /// <summary>The type, when inference gives a value of it as a scalar; otherwise null, which the caller makes unknown.</summary>
    public static ScalarType? Modelled(ScalarType? type) => type is not null && IsModelled(type.Type) ? type : null;

    /// <summary>Whether a scalar is <c>nvarchar(max)</c>, which holds any JSON text unchanged.</summary>
    public static bool IsNVarCharMax(ScalarShape scalar) => scalar.BuiltIn is { IsMax: true } type && type.Type == BuiltInType.NVarChar;

    /// <summary>The type an IR scalar's SQL text names, when inference models it: <c>decimal(19,2)</c>.</summary>
    public static ScalarType? Parse(string? sql) => Modelled(ScalarType.Parse(sql));

    /// <param name="facts">What is known of the values.</param>
    /// <param name="resolved">Their type, which <paramref name="facts"/> holds too.</param>
    /// <param name="at">Where the value is read.</param>
    /// <param name="withFacets">Whether the type's and the column's facts are kept.</param>
    private static Shape ToShape(ColumnFacts facts, ResolvedType resolved, SourceLocation at, bool withFacets)
    {
        var text = resolved.Spelt;
        var type = resolved.Type;

        // A json parameter is text the caller sends, and a driver returns a json column as text. A json column inside FOR
        // JSON or a constructor is live JSON instead, which the Typer decides before this.
        if (type == BuiltInType.Json)
            return new JsonTextShape();
        if (type is null || !Scalars.TryGetValue(type, out var scalar))
            return new UnknownShape(IrUnknownReason.UnsupportedSqlType, $"The type {text} is not modelled yet.", at);
        return new ScalarShape(new IrScalar(scalar.Kind, scalar.Format, text, withFacets ? Facets(facts) : null), resolved.Scalar);
    }

    private static UnknownShape Unsupported(SqlType? type, SourceLocation at) =>
        // XML, CLR types, and alias types that cannot be resolved for certain.
        new(IrUnknownReason.UnsupportedSqlType, $"The type {type?.ToString() ?? "(none)"} is not modelled yet.", at);

    private static IrFacets? Facets(ColumnFacts facts)
    {
        var irFacets = new IrFacets(
            Length: facts.Length is { } length
                ? new IrLength(length.Value, length.IsFixed, length.Unit == TextUnit.Utf16 ? IrLengthUnit.Utf16 : IrLengthUnit.Bytes, Encoding(length.Encoding), Source(length.From))
                : null,
            MinLength: facts.MinLength is { } minLength ? new IrMinLength(minLength.Value, Source(minLength.From)) : null,
            Digits: facts.Digits is { } digits ? new IrDigits(digits.Precision, digits.Scale, Source(digits.From)) : null,
            Min: Bound(facts.Min),
            Max: Bound(facts.Max),
            Values: facts.Values is { } values
                ? new IrValueSet(
                    values.Numbers is { } numbers
                        ? [.. numbers.Select(n => new IrValue(n.ToString(), IsString: false))]
                        // Never null: a value set holds numbers or strings (ValueSetFact).
                        : [.. values.Strings!.Select(s => new IrValue(s, IsString: true))],
                    values.Comparison switch
                    {
                        ValueComparison.Exact => IrValueMatch.Exact,
                        ValueComparison.Padded => IrValueMatch.Padded,
                        ValueComparison.Canonical => IrValueMatch.Canonical,
                        _ => IrValueMatch.Collation,
                    },
                    [.. values.From.Select(Source)],
                    values.Fold?.Name)
                : null);
        return irFacets.IsEmpty ? null : irFacets;
    }

    /// <summary>A fold as the IR holds it.</summary>
    public static IrFold Fold(CollationFold fold) => new(fold.Ignored, fold.Classes);

    private static IrBound? Bound(BoundFact? bound) => bound is null ? null : new IrBound(bound.Value.ToString(), bound.Inclusive, Source(bound.From));

    private static IrByteEncoding Encoding(TextEncoding encoding) => encoding switch
    {
        TextEncoding.SingleByte => IrByteEncoding.SingleByte,
        TextEncoding.Utf8 => IrByteEncoding.Utf8,
        TextEncoding.DoubleByte => IrByteEncoding.DoubleByte,
        _ => IrByteEncoding.Unknown,
    };

    /// <summary>
    /// A type by its name; a check by its schema-qualified name, or its location when it has none. A temporary table's or
    /// table variable's check is named by its table (<c>#totals.ck_amount</c>), since it belongs to no schema of the project.
    /// </summary>
    private static IrFactSource Source(FactSource from) => from switch
    {
        TypeSource type => new IrFactSource(IrFactSourceKind.Type, type.Type),
        CheckSource { Check.Name: { } name, Table: DeclaredTable table } => new IrFactSource(IrFactSourceKind.Check, $"{table.Name}.{name}"),
        CheckSource { Check.Name: { } name, Table: Table table } => new IrFactSource(IrFactSourceKind.Check, $"{table.Name.DatabasePrefix}{table.Name.Schema}.{name}"),
        CheckSource check => new IrFactSource(IrFactSourceKind.Check, check.Check.At.ToString()),
    };
}
