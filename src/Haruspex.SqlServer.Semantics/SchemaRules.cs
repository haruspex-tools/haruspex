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
    public static bool IsNeverNull(DatabaseModel model, ITable table, Column column)
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
    public static IEnumerable<UniqueKey> EnforcedKeys(ITable table)
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
                if (BuiltInType.SystemAlias(type) is { } system)
                    return system;

                // An unqualified name resolves in the caller's default schema and then in dbo, and the source does not
                // say whose. With exactly one type of that name in the project, a script that deployed used that one.
                // CLR types count, so that one can't be mistaken for an alias of the same name.
                var candidates = type.Schema is { } schema
                    ? model.FindUserType(new ObjectName(schema, type.Name)) is { } found ? [found] : []
                    : model.UserTypes.Where(t => t.Name.Name.Equals(type.Name, StringComparison.OrdinalIgnoreCase)).ToList();
                if (candidates is not [AliasType { BaseType: { Kind: SqlTypeKind.BuiltIn } builtIn } alias])
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

        // Floating point has no range worth claiming (any finite double); bit is a boolean in the IR; other kinds have no
        // facets here, and a string of max no length to claim.
        return type.Scalar switch
        {
            { IsInteger: true, Type: { Least: { } least, Greatest: { } greatest } } =>
                new ColumnFacts(type, Min: new BoundFact(least, true, from), Max: new BoundFact(greatest, true, from)),
            { IsDecimal: true } d => Exact(type, from, d.Precision, d.Scale, ExactDecimal.Largest(d.Precision, d.Scale)),
            { IsMoney: true, Type: { Least: { } least, Greatest: { } greatest } } m => Exact(type, from, m.Precision, m.Scale, greatest, least),
            { IsString: true, IsMax: false } text => new ColumnFacts(type, Length: new LengthFact(
                text.Length,
                IsFixed: !text.IsVarying,
                text.IsNational ? TextUnit.Utf16 : TextUnit.Bytes,
                text.IsNational ? TextEncoding.Unknown : EncodingOf(collation),
                from)),
            _ => new ColumnFacts(type),
        };
    }

    /// <summary>
    /// Everything the schema guarantees about a column's non-null values: its type's facets narrowed by its CHECK
    /// constraints (see <see cref="CheckReader"/>). Null type when the column's type cannot be resolved.
    /// </summary>
    public static ColumnFacts ColumnFacts(DatabaseModel model, ITable table, Column column)
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

    private static ColumnFacts Exact(ResolvedType type, FactSource from, int precision, int scale, ExactDecimal max, ExactDecimal? min = null) =>
        new(type,
            Digits: new DigitsFact(precision, scale, from),
            Min: new BoundFact(min ?? max.Negate(), true, from),
            Max: new BoundFact(max, true, from));

    /// <summary>The facts true of every value that satisfies both: the tighter bound, the longer minimum length, and the narrower set.</summary>
    private static ColumnFacts Narrow(ColumnFacts facts, ColumnFacts proved) => facts with
    {
        Min = Bounds.Tighter(facts.Min, proved.Min, BoundSide.Lower, m => m.Bound),
        Max = Bounds.Tighter(facts.Max, proved.Max, BoundSide.Upper, m => m.Bound),
        MinLength = proved.MinLength is { } m && (facts.MinLength is null || m.Value > facts.MinLength.Value) ? m : facts.MinLength,
        Values = Intersect(facts.Values, proved.Values),
    };

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
            (facts.Min is not { } min || Bounds.Admits(min.Bound, n, BoundSide.Lower))
            && (facts.Max is not { } max || Bounds.Admits(max.Bound, n, BoundSide.Upper))
            && (facts.Digits is not { } digits || n.Scale <= digits.Scale);

        return facts with { Values = facts.Values with { Numbers = [.. numbers.Where(Inside).Distinct().Order()] } };
    }
}
