using System.Numerics;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// What the schema implies beyond what it declares. The model holds declarations; the rules that turn them into
/// facts about values live here, so that lint, diff and inference all read a schema the same way.
/// </summary>
public static class SchemaRules
{
    /// <summary>
    /// Whether SQL Server guarantees the column is never null: it is declared NOT NULL, it is in the primary key
    /// (which implies NOT NULL), it is an IDENTITY column (which cannot be nullable), or it declares neither NULL
    /// nor NOT NULL and its alias type is NOT NULL (<c>sysname</c> is).
    /// </summary>
    /// <remarks>
    /// <para>A column declared with neither NULL nor NOT NULL is nullable under the default ANSI settings; with
    /// ANSI_NULL_DFLT_OFF it would not be, so nullable is the claim that holds either way. A computed column is
    /// read by the same rules today; its expression is not yet used to prove it non-null.</para>
    /// <para>An alias type's NOT NULL applies whatever the ANSI settings, and an explicit NULL on the column
    /// overrides it (observed on SQL Server 2025; kept as a differential test). A CHECK constraint never proves a
    /// column non-null, because a CHECK passes when its condition is UNKNOWN, as it is for null.</para>
    /// </remarks>
    public static bool IsNeverNull(DatabaseModel model, Table table, Column column)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(column);
        return column.Nullability == DeclaredNullability.NotNull
            || table.IsInPrimaryKey(column.Name)
            || column.IsIdentity
            || (column.Nullability == DeclaredNullability.NotDeclared
                && ResolveType(model, column.Type)?.AliasNullability == DeclaredNullability.NotNull);
    }

    /// <summary>
    /// The table's keys that hold for every row: its primary key, unique constraints and unique indexes, less any an
    /// <c>ALTER INDEX … DISABLE</c> may have disabled, since rows written while it is disabled need not be unique. Each is a
    /// set of columns that no two rows share; SQL Server counts NULLs as equal in them, so at most one row has NULL in all
    /// of a key's nullable columns (observed).
    /// </summary>
    public static IEnumerable<UniqueKey> EnforcedKeys(Table table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return table.UniqueKeys.Where(k => k.DisabledAt is null);
    }

    /// <summary>
    /// The built-in type a declared type stands for, resolving an alias type (<c>sysname</c> included); null when
    /// it is none or cannot be resolved for certain (a CLR type, <c>xml</c>, a name that is not defined, or an
    /// unqualified name that more than one schema defines).
    /// </summary>
    public static ResolvedType? ResolveType(DatabaseModel model, SqlType? type)
    {
        ArgumentNullException.ThrowIfNull(model);
        switch (type)
        {
            case { Kind: SqlTypeKind.BuiltIn }:
                return new ResolvedType(type, null, DeclaredNullability.NotDeclared);
            case { Kind: SqlTypeKind.Named }:
                // sysname is the system's own alias: nvarchar(128), NOT NULL unless a column says NULL. System types
                // are found before user types for an unqualified name.
                if (type.Name.Equals("sysname", StringComparison.OrdinalIgnoreCase)
                    && (type.Schema is null || type.Schema.Equals("sys", StringComparison.OrdinalIgnoreCase)))
                {
                    return new ResolvedType(new SqlType(SqlTypeKind.BuiltIn, "nvarchar", ["128"]), "sysname", DeclaredNullability.NotNull);
                }

                // An unqualified name resolves in the caller's default schema and then in dbo, and the source does not
                // say whose. With exactly one type of that name in the project, a script that deployed used that one.
                // CLR types count, so that one can't be mistaken for an alias of the same name.
                var candidates = type.Schema is { } schema
                    ? model.FindUserType(new ObjectName(schema, type.Name)) is { } found ? [found] : []
                    : model.UserTypes.Where(t => t.Name.Name.Equals(type.Name, StringComparison.OrdinalIgnoreCase)).ToList();
                if (candidates is not [{ BaseType: { Kind: SqlTypeKind.BuiltIn } builtIn } alias])
                    return null;
                return new ResolvedType(builtIn, alias.Name.ToString(), alias.Nullability);
            default:
                return null;
        }
    }

    /// <summary>What a type alone guarantees about its values: a string's length, an exact number's digits and range.</summary>
    /// <param name="type">The resolved type.</param>
    /// <param name="collation">The collation a column declares, for the encoding of <c>char</c> and <c>varchar</c>; null when none is declared.</param>
    public static ColumnFacts TypeFacts(ResolvedType type, string? collation)
    {
        ArgumentNullException.ThrowIfNull(type);
        var from = new TypeSource(type.SourceName);
        var arguments = type.BuiltIn.Arguments;

        switch (type.BuiltIn.Name)
        {
            case "tinyint":
                return Range(type, from, 0, byte.MaxValue);
            case "smallint":
                return Range(type, from, short.MinValue, short.MaxValue);
            case "int":
                return Range(type, from, int.MinValue, int.MaxValue);
            case "bigint":
                return Range(type, from, long.MinValue, long.MaxValue);
            case "decimal" or "numeric":
                // decimal alone is decimal(18, 0), and decimal(p) is decimal(p, 0).
                if (!TryInt(arguments, 0, 18, out var precision) || !TryInt(arguments, 1, 0, out var scale))
                    return new ColumnFacts(type);
                return Exact(type, from, precision, scale, ExactDecimal.Largest(precision, scale));
            case "money":
                return Exact(type, from, 19, 4, ExactDecimal.Of(long.MaxValue, 4), ExactDecimal.Of(long.MinValue, 4));
            case "smallmoney":
                return Exact(type, from, 10, 4, ExactDecimal.Of(int.MaxValue, 4), ExactDecimal.Of(int.MinValue, 4));
            case "char" or "varchar" or "nchar" or "nvarchar":
                // char and nchar alone are char(1) and nchar(1); (max) has no length to claim.
                if (!TryInt(arguments, 0, 1, out var length))
                    return new ColumnFacts(type);
                var unicode = type.BuiltIn.Name.StartsWith('n');
                return new ColumnFacts(type, Length: new LengthFact(
                    length,
                    IsFixed: type.BuiltIn.Name is "char" or "nchar",
                    unicode ? TextUnit.Utf16 : TextUnit.Bytes,
                    unicode ? TextEncoding.Unknown : EncodingOf(collation),
                    from));
            default:
                // Floating point has no range worth claiming (any finite double); other kinds have no facets here.
                return new ColumnFacts(type);
        }
    }

    /// <summary>
    /// Everything the schema guarantees about a column's non-null values: its type's facets narrowed by its CHECK
    /// constraints (see <see cref="CheckReader"/>). Null type when the column's type cannot be resolved.
    /// </summary>
    public static ColumnFacts ColumnFacts(DatabaseModel model, Table table, Column column)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(column);
        if (column.IsComputed || ResolveType(model, column.Type) is not { } type)
            return new ColumnFacts(null);

        var facts = TypeFacts(type, column.Collation);
        foreach (var check in table.Checks)
        {
            if (CheckReader.Read(table, column, type, check) is { } proved)
                facts = Narrow(facts, proved);
        }

        return FilterValues(facts);
    }

    /// <summary>
    /// The encoding of <c>char</c> and <c>varchar</c> data under a collation. Non-Unicode data uses the collation's
    /// code page; of those SQL Server supports, only 932 (Japanese), 936 and 950 (Chinese) and 949 (Korean) are
    /// double-byte, and a <c>_UTF8</c> collation stores UTF-8. Every other code page (1250 to 1258, 874) is
    /// single-byte. Unicode-only collations cannot be used for <c>char</c> or <c>varchar</c> at all.
    /// </summary>
    public static TextEncoding EncodingOf(string? collation) => collation switch
    {
        null => TextEncoding.Unknown,
        _ when collation.EndsWith("_UTF8", StringComparison.OrdinalIgnoreCase) => TextEncoding.Utf8,
        _ when collation.StartsWith("Japanese", StringComparison.OrdinalIgnoreCase)
            || collation.StartsWith("Chinese", StringComparison.OrdinalIgnoreCase)
            || collation.StartsWith("Korean", StringComparison.OrdinalIgnoreCase) => TextEncoding.DoubleByte,
        _ => TextEncoding.SingleByte,
    };

    private static ColumnFacts Range(ResolvedType type, FactSource from, BigInteger min, BigInteger max) =>
        new(type, Min: new BoundFact(ExactDecimal.Of(min, 0), true, from), Max: new BoundFact(ExactDecimal.Of(max, 0), true, from));

    private static ColumnFacts Exact(ResolvedType type, FactSource from, int precision, int scale, ExactDecimal max, ExactDecimal? min = null) =>
        new(type,
            Digits: new DigitsFact(precision, scale, from),
            Min: new BoundFact(min ?? max.Negate(), true, from),
            Max: new BoundFact(max, true, from));

    private static bool TryInt(IReadOnlyList<string> arguments, int index, int absent, out int value)
    {
        if (arguments.Count <= index)
        {
            value = absent;
            return true;
        }

        return int.TryParse(arguments[index], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    /// <summary>The facts true of every value that satisfies both: the tighter bound, the longer minimum length, and the narrower set.</summary>
    private static ColumnFacts Narrow(ColumnFacts facts, ColumnFacts proved) => facts with
    {
        Min = Tighter(facts.Min, proved.Min, lower: true),
        Max = Tighter(facts.Max, proved.Max, lower: false),
        MinLength = proved.MinLength is { } m && (facts.MinLength is null || m.Value > facts.MinLength.Value) ? m : facts.MinLength,
        Values = Intersect(facts.Values, proved.Values),
    };

    private static BoundFact? Tighter(BoundFact? a, BoundFact? b, bool lower)
    {
        if (a is null || b is null)
            return a ?? b;
        var order = a.Value.CompareTo(b.Value);
        if (order == 0)
            return a.Inclusive ? b : a; // an exclusive bound at the same value is the tighter one
        return (order > 0) == lower ? a : b;
    }

    private static ValueSetFact? Intersect(ValueSetFact? a, ValueSetFact? b)
    {
        if (a is null || b is null)
            return a ?? b;

        // Two sets of strings compared under a collation can't be intersected by comparing the strings exactly
        // ('Open' and 'open' may be equal), so the first is kept: it alone is still true.
        if (a.Numbers is null || b.Numbers is null)
            return a;
        return a with { Numbers = [.. a.Numbers.Where(b.Numbers.Contains)], From = [.. a.From, .. b.From] };
    }

    /// <summary>Drops numbers a value set lists that its bounds rule out, so the set says only what can be stored.</summary>
    private static ColumnFacts FilterValues(ColumnFacts facts)
    {
        if (facts.Values?.Numbers is not { } numbers)
            return facts;

        bool Inside(ExactDecimal n) =>
            (facts.Min is not { } min || (min.Inclusive ? n >= min.Value : n > min.Value))
            && (facts.Max is not { } max || (max.Inclusive ? n <= max.Value : n < max.Value))
            && (facts.Digits is not { } digits || n.Scale <= digits.Scale);

        return facts with { Values = facts.Values with { Numbers = [.. numbers.Where(Inside).Distinct().Order()] } };
    }
}
