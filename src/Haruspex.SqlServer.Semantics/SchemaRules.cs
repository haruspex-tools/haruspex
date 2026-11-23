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
    /// overrides it (observed on SQL Server 2025; kept as a differential test). So does a <c>rowversion</c>'s, which the server
    /// fills in every row (observed: A_rowversion_column_declared_without_null_is_not_null_whatever_the_ansi_settings). A CHECK constraint never proves a
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
                && ResolveType(model, column.Type) is { } type
                && (type.AliasNullability == DeclaredNullability.NotNull || type.Scalar?.Family == TypeFamily.RowVersion));
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

                // An unqualified name resolves as any name does where it is written (SchemaSearch): in a module's schema and
                // then in dbo, or, in a table's column, the creator's default schema and then dbo, whose the source doesn't
                // say; with exactly one type of that name in the project, a script that deployed used that one. CLR types
                // count, so that one can't be mistaken for an alias of the same name.
                if (FindUserType(model, type) is not AliasType { BaseType: { Kind: SqlTypeKind.BuiltIn } builtIn } alias)
                    return null;
                return new ResolvedType(builtIn, alias.Name.ToString(), alias.Nullability);
            default:
                return null;
        }
    }

    /// <summary>
    /// The table type a parameter or variable is declared with, found as an alias type is (<see cref="ResolveType"/>): by its
    /// schema when it names one, and otherwise as <see cref="SchemaNames.Resolve"/> finds it from where it is written; null when
    /// it is no table type the model defines.
    /// </summary>
    public static TableType? ResolveTableType(DatabaseModel model, SqlType? type)
    {
        ArgumentNullException.ThrowIfNull(model);
        return type is { Kind: SqlTypeKind.Named } ? FindUserType(model, type) as TableType : null;
    }

    /// <summary>
    /// The user type a name refers to, by its schema, or as the one of that name found from where it is written (observed:
    /// An_alias_type_in_a_module_is_looked_for_in_the_modules_schema); null when there is none or several.
    /// </summary>
    private static UserType? FindUserType(DatabaseModel model, SqlType type) =>
        type.Schema is { } schema
            ? model.FindUserType(new ObjectName(schema, type.Name))
            : SchemaNames.Resolve(model.UserTypes, type.Name, SchemaSearch.Of(type)) is OneObject { Named: UserType one } ? one : null;

    /// <summary>The bytes of a <c>rowversion</c>, which SQL Server stores as <c>binary(8)</c>.</summary>
    public static int RowVersionBytes => 8;

    /// <summary>What a type alone guarantees about its values: a string's or binary value's length, an exact number's digits and range.</summary>
    /// <param name="type">The resolved type.</param>
    /// <param name="collation">The collation a column declares, for the encoding of <c>char</c> and <c>varchar</c>; null when none is declared.</param>
    public static ColumnFacts TypeFacts(ResolvedType type, string? collation)
    {
        ArgumentNullException.ThrowIfNull(type);
        var from = new TypeSource(type.SourceName);

        // Floating point has no range worth claiming (any finite double); bit is a boolean in the IR; other kinds have no
        // facets here, and a string or binary value of max no length to claim. A binary value's length counts bytes, which
        // encode nothing, and a rowversion is 8 of them, binary(8) as SQL Server documents it.
        return type.Scalar switch
        {
            { Family: TypeFamily.Binary, IsMax: false } bytes => new ColumnFacts(type, Length: new LengthFact(bytes.Length, IsFixed: !bytes.IsVarying, TextUnit.Bytes, TextEncoding.Unknown, from)),
            { Family: TypeFamily.RowVersion } => new ColumnFacts(type, Length: new LengthFact(RowVersionBytes, IsFixed: true, TextUnit.Bytes, TextEncoding.Unknown, from)),
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

        var collation = EffectiveCollation(model, table, column);
        var facts = TypeFacts(type, collation);

        if (facts.Length is { IsFixed: true } length && MayCutPadding(model, table, column, type))
            facts = facts with { Length = length with { IsFixed = false } };
        foreach (var check in table.Checks)
        {
            if (CheckReader.Read(table, column, type, check, collation) is { } proved)
                facts = Narrow(facts, proved);
        }

        return FilterValues(facts);
    }

    /// <summary>
    /// Whether a column of <c>char(n)</c> or <c>binary(n)</c> may hold values shorter than n, its padding cut: a nullable one of a
    /// table created while <c>ANSI_PADDING</c> is off does (<see cref="ScalarType.IsPaddingCutWithoutAnsiPadding"/>). A script may
    /// set it off before a <c>CREATE TABLE</c>, and the caller's session decides it for a temporary table a body makes, neither of
    /// which the model follows; a DACPAC records it per table, which is not read either, so the rule is the same for every
    /// source and only claims less where padding was on. A column that is never null pads under either setting, and so do a table
    /// type's columns and a table variable's, of a type or not (observed:
    /// A_table_type_created_with_ansi_padding_off_pads_its_nullable_char_and_binary_columns,
    /// A_nullable_char_column_a_body_creates_is_cut_under_its_callers_ansi_padding_off_but_a_table_variable_s_pads).
    /// </summary>
    private static bool MayCutPadding(DatabaseModel model, ITable table, Column column, ResolvedType type) =>
        type.Scalar is { IsPaddingCutWithoutAnsiPadding: true }
        && table is not (TableType or DeclaredTable { IsVariable: true })
        && !IsNeverNull(model, table, column);

    /// <summary>
    /// The collation a column's strings compare under: its own <c>COLLATE</c>, or for a table of the schema the database's default
    /// (<see cref="DatabaseModel.DefaultCollation"/>); null when neither is known. A table type's columns take the database's too,
    /// fixed when the type is created there, and so do a parameter's and a variable's of the type (observed:
    /// <c>A_table_type_s_column_takes_the_database_s_collation</c>). A body's own table (<c>#totals</c>, <c>@rows</c>) takes no
    /// default here: a temporary table's columns take <c>tempdb</c>'s, which the model doesn't know.
    /// </summary>
    public static string? EffectiveCollation(DatabaseModel model, ITable table, Column column)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(column);
        return column.Collation ?? (table is Table or TableType or DeclaredTable { Type: not null } ? model.DefaultCollation : null);
    }

    /// <summary>
    /// The encoding of <c>char</c> and <c>varchar</c> data under a collation, from the code page SQL Server gives it
    /// (<see cref="CollationCatalogue.CodePage"/>): of those it supports, only 932 (Japanese), 936 and 950 (Chinese) and 949 (Korean) are
    /// double-byte, and a <c>_UTF8</c> collation stores UTF-8; every other code page (1250 to 1258, 874, 850, 437) is
    /// single-byte. Unknown for a Unicode-only collation, which <c>char</c> and <c>varchar</c> can't use, and for a name no
    /// collation has, which is never read as some other collation's: a misspelt <c>_UTF8</c> name read as single-byte would type
    /// a <c>char(n)</c> as exactly n characters.
    /// </summary>
    public static TextEncoding EncodingOf(string? collation) => (collation is null ? null : CollationCatalogue.CodePage(collation)) switch
    {
        null or CollationCatalogue.UnicodeOnly => TextEncoding.Unknown,
        CollationCatalogue.Utf8 => TextEncoding.Utf8,
        932 or 936 or 949 or 950 => TextEncoding.DoubleByte,
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
        MinLength = proved.MinLength is { } m && (facts.MinLength is null || !facts.MinLength.Implies(m)) ? m : facts.MinLength,
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
