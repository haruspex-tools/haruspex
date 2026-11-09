using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;

namespace Haruspex.SqlServer.Inference;

/// <summary>Maps SQL Server types to the IR's logical types, keeping the SQL type as written.</summary>
/// <remarks>
/// The model says what a type is (<see cref="SqlType"/>), and Semantics what it and the column's constraints
/// guarantee (<see cref="ColumnFacts"/>); what they become in the IR is inference's choice, so the mapping lives here.
/// </remarks>
internal static class SqlTypes
{
    /// <summary>A parameter's or variable's type: its kind, with an alias resolved, but no facets.</summary>
    /// <remarks>
    /// A parameter's facets describe the value after SQL Server has converted what the caller sent (a string too long
    /// for <c>nvarchar(20)</c> is cut short without an error), not what a caller may send, so they are left for the
    /// generated client module to state as an input rule.
    /// </remarks>
    public static Shape ToShape(DatabaseModel model, SqlType? type, SourceLocation at) =>
        SchemaRules.ResolveType(model, type) is { } resolved
            ? ToShape(new ColumnFacts(resolved), at, withFacets: false)
            : Unsupported(type, at);

    /// <summary>A column's type, with every fact Semantics proved about its values.</summary>
    public static Shape ToShape(ColumnFacts facts, SqlType? declared, SourceLocation at) =>
        facts.Type is null ? Unsupported(declared, at) : ToShape(facts, at, withFacets: true);

    private static Shape ToShape(ColumnFacts facts, SourceLocation at, bool withFacets)
    {
        var type = facts.Type!.BuiltIn;
        var text = Text(type);
        var irFacets = withFacets ? Facets(facts) : null;
        ScalarShape Scalar(IrScalarKind kind, string? format) => new(new IrScalar(kind, format, text, irFacets));

        return type.Name switch
        {
            "tinyint" => Scalar(IrScalarKind.Number, IrFormats.Uint8),
            "smallint" => Scalar(IrScalarKind.Number, IrFormats.Int16),
            "int" => Scalar(IrScalarKind.Number, IrFormats.Int32),
            "bigint" => Scalar(IrScalarKind.Number, IrFormats.Int64),
            "decimal" or "numeric" or "money" or "smallmoney" => Scalar(IrScalarKind.Number, IrFormats.Decimal),
            "float" or "real" => Scalar(IrScalarKind.Number, IrFormats.Float),
            "bit" => Scalar(IrScalarKind.Boolean, null),
            "char" or "varchar" or "nchar" or "nvarchar" => Scalar(IrScalarKind.String, null),
            "date" => Scalar(IrScalarKind.Date, null),
            "time" => Scalar(IrScalarKind.Time, null),
            "datetime" or "datetime2" or "smalldatetime" => Scalar(IrScalarKind.Datetime, IrFormats.Local),
            "datetimeoffset" => Scalar(IrScalarKind.Datetime, IrFormats.Offset),
            "uniqueidentifier" => Scalar(IrScalarKind.Uuid, null),
            _ => new UnknownShape(IrUnknownReasons.UnsupportedSqlType, $"The type {text} is not modelled yet.", at),
        };
    }

    private static UnknownShape Unsupported(SqlType? type, SourceLocation at) =>
        // XML, CLR types, and alias types that cannot be resolved for certain.
        new(IrUnknownReasons.UnsupportedSqlType, $"The type {type?.ToString() ?? "(none)"} is not modelled yet.", at);

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
                        : [.. values.Strings!.Select(s => new IrValue(s, IsString: true))],
                    values.Comparison == ValueComparison.Exact ? IrValueMatch.Exact : IrValueMatch.Collation,
                    [.. values.From.Select(Source)])
                : null);
        return irFacets.IsEmpty ? null : irFacets;
    }

    private static IrBound? Bound(BoundFact? bound) => bound is null ? null : new IrBound(bound.Value.ToString(), bound.Inclusive, Source(bound.From));

    private static IrByteEncoding Encoding(TextEncoding encoding) => encoding switch
    {
        TextEncoding.SingleByte => IrByteEncoding.SingleByte,
        TextEncoding.Utf8 => IrByteEncoding.Utf8,
        TextEncoding.DoubleByte => IrByteEncoding.DoubleByte,
        _ => IrByteEncoding.Unknown,
    };

    /// <summary>A type by its name; a check by its schema-qualified name, or its location when it has none.</summary>
    private static IrFactSource Source(FactSource from) => from switch
    {
        TypeSource type => new IrFactSource(IrFactSources.Type, type.Type),
        CheckSource { Check.Name: { } name } check => new IrFactSource(IrFactSources.Check, $"{check.Table.Name.Schema}.{name}"),
        CheckSource check => new IrFactSource(IrFactSources.Check, check.Check.At.ToString()),
        _ => throw new ArgumentOutOfRangeException(nameof(from), from, "Unknown fact source."),
    };

    /// <summary>The type as written, normalised: <c>decimal(19,2)</c>, <c>nvarchar(max)</c>.</summary>
    private static string Text(SqlType type)
    {
#pragma warning disable CA1308 // The IR writes SQL type names in lower case, as T-SQL is usually written; this is not a comparison.
        return type.Arguments.Count == 0
            ? type.Name
            : type.Name + "(" + string.Join(",", type.Arguments.Select(a => a.ToLowerInvariant())) + ")";
#pragma warning restore CA1308
    }
}
