using Haruspex.Collections;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics.Collations;
using Haruspex.SqlServer.Syntax;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// Reads what a CHECK constraint proves about one column's non-null values. It understands a few forms and ignores
/// everything else, which is always safe: an ignored constraint leaves the type wider than it could be, never
/// narrower than the data.
/// </summary>
/// <remarks>
/// <para><b>Only a trusted constraint guarantees anything.</b> A constraint checks no row while it is disabled
/// (<c>ALTER TABLE … NOCHECK CONSTRAINT</c>), and one added <c>WITH NOCHECK</c> never checked the rows already
/// there; either way SQL Server marks it not trusted, and the data may break it. So a constraint that the source
/// may have disabled, by a NOCHECK anywhere (a procedure body included), is not read (unless
/// <c>WITH CHECK CHECK CONSTRAINT</c>, which validates every row, follows in the same script; Haruspex.SqlServer.Sources
/// records which), a constraint added by <c>ALTER TABLE</c> is not
/// read at all, and nor is one that is <c>NOT FOR REPLICATION</c>, which a replication agent's writes bypass. All
/// observed, and kept as CheckConstraintObservationTests. What the source can't show is a live database that has
/// drifted from it: a constraint disabled by hand on the server, or by a NOCHECK in dynamic SQL, is outside static analysis, and a project's facts
/// hold only for a database deployed from it and not changed since.</para>
/// <para><b>Names that differ only in case.</b> Nothing is read for a column when the table has another whose name differs from
/// its only in case, as a database of a case-sensitive collation allows: a constraint's names are matched ignoring case, so it
/// would be read for both (decision 0107).</para>
/// <para><b>What is read.</b> A constraint that refers to this column and no other, trusted as above, whose condition is built from these by AND (each conjunct on its own) and OR (only of
/// value sets, or of anything read with <c>col IS NULL</c>):</para>
/// <list type="bullet">
/// <item><c>col IN (literal, …)</c> and <c>col = literal</c>: a value set;</item>
/// <item><c>col BETWEEN a AND b</c> and <c>col &lt; literal</c> (and <c>&lt;=</c>, <c>&gt;</c>, <c>&gt;=</c>, either way round): bounds, for numbers;</item>
/// <item><c>len(col) &gt; n</c>, <c>len(col) &gt;= n</c>, <c>len(col) &lt;&gt; 0</c> and <c>col &lt;&gt; ''</c>: a minimum length, for strings;</item>
/// <item><c>col IS NULL</c>: nothing of its own, but FALSE for every non-null value.</item>
/// </list>
/// <para><b>Why each is sound.</b> A CHECK rejects a row only when its condition is FALSE; TRUE and UNKNOWN pass. For
/// a non-null value, each form above is TRUE or FALSE, never UNKNOWN (a NULL among the literals would make
/// <c>IN</c> UNKNOWN, so such a list is not read). So for a non-null value: <c>A AND B</c> passing means neither is
/// FALSE, so each is TRUE, and a conjunct that is not understood can be skipped; <c>A OR B</c> passing means one is
/// TRUE, so it is read only when every side is a value set, and gives their union. <c>col IS NULL</c> is FALSE for a non-null
/// value, so it is never the TRUE side: <c>A OR col IS NULL</c> passes a non-null value when A does and only then, and proves what A
/// proves, a bound or a minimum length as much as a set (observed: An_in_list_or_is_null_check_refuses_a_value_outside_the_set);
/// and <c>A AND col IS NULL</c> passes none, which proves nothing read here. Null is never ruled out: it makes every other form
/// UNKNOWN, which passes, and <c>col IS NULL</c> TRUE.</para>
/// <para><b>Comparisons are exact.</b> A literal is compared with an exact numeric column only when it has no more
/// decimal places than the column's scale and no more integer digits than its precision leaves. Then both convert
/// to a common decimal type without rounding. Otherwise the result type of mixing precisions and scales (capped at
/// 38 digits, losing scale beyond it) or a conversion to the column's type could round the literal, and rather than
/// model those rules the constraint is not read. (A <c>money</c> column was observed to compare exactly with a
/// literal of five decimal places, so for it this is caution, not a known rounding; see
/// CheckConstraintObservationTests.) Floating-point columns are not read at all: a literal is rounded to the column's binary
/// type before comparison, and FOR JSON writes 16 significant digits, so a bound would need care this does not
/// take. Strings compare under the column's collation with trailing spaces ignored, so a value set of strings is
/// never exact: <c>'Open '</c> passes <c>IN ('open')</c> in the default case-insensitive collation, and even a
/// binary collation ignores trailing spaces (both observed; see the differential tests'
/// CheckConstraintObservationTests). Under a binary collation it is <see cref="ValueComparison.Padded"/> (exact but
/// for trailing spaces); for a <c>char</c> or <c>varchar</c> column of a collation whose fold is proven,
/// <see cref="ValueComparison.Canonical"/>; and otherwise <see cref="ValueComparison.Collation"/>; see StringComparisonOf. A
/// <c>COLLATE</c> on the column or a literal in the condition decides the comparison instead of the column's collation, so a set
/// compared so is always <see cref="ValueComparison.Collation"/> (observed: CheckConstraintObservationTests).</para>
/// <para><b>Minimum length.</b> <c>LEN</c> does not count trailing spaces, and counts a character (a surrogate pair
/// under a supplementary-character collation, several bytes in UTF-8) as one, so a value's UTF-16 length is at least its
/// <c>LEN</c>, and under a supplementary-character collation its code points are (<see cref="MinLengthFact.InCodePoints"/>). <c>col &lt;&gt; ''</c> is FALSE for an empty string (and for spaces, which compare equal to it), so
/// the value has at least one character.</para>
/// </remarks>
public static class CheckReader
{
    /// <summary>What <paramref name="check"/> proves about <paramref name="column"/>, or null when it proves nothing this reads.</summary>
    /// <returns>Facts with only <see cref="ColumnFacts.Min"/>, <see cref="ColumnFacts.Max"/>, <see cref="ColumnFacts.MinLength"/> and <see cref="ColumnFacts.Values"/> set.</returns>
    /// <param name="table">The column's table.</param>
    /// <param name="column">The column.</param>
    /// <param name="type">The column's type, resolved.</param>
    /// <param name="check">The constraint.</param>
    /// <param name="collation">The collation the column's strings compare under (<see cref="SchemaRules.EffectiveCollation"/>); null when unknown.</param>
    public static ColumnFacts? Read(ITable table, Column column, ResolvedType type, CheckConstraint check, string? collation)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(column);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(check);

        // A disabled or NOT FOR REPLICATION constraint does not check every row.
        if (check.DisabledAt is not null || check.IsNotForReplication)
            return null;

        // A column another's name differs from only in case: the constraint's names can't be told apart without the database's
        // collation, which haruspex doesn't model, and read ignoring case one column's CHECK would narrow both (decision 0107).
        if (table.Columns.Any(other => Same(other.Name, column.Name) && !other.Name.Equals(column.Name, StringComparison.Ordinal)))
            return null;

        // Only constraints on this column alone.
        if (check.Column is { } declaredOn && !Same(declaredOn, column.Name))
            return null;
        var references = new ColumnReferences();
        check.Expression.Accept(references);
        if (references.Names.Count == 0 || references.Names.Any(n => n is null || !Same(n, column.Name)))
            return null;

        if (FamilyOf(type) is not { } family)
            return null;

        var reader = new Reader(column.Name, family, new CheckSource(table, check));
        // A condition no non-null value passes would make the column always null, which is not claimed: it proves nothing here.
        var proved = reader.Condition(check.Expression) is { PassesNoValue: false } read ? read : null;
        // A COLLATE on the column or a literal it is compared with decides the comparison, by SQL Server's precedence, so the
        // column's collation says nothing of it (observed: A_collate_in_a_check_decides_how_it_compares): such a set is a note.
        return proved?.ToFacts(
            type,
            reader.Source,
            reader.Collated ? (ValueComparison.Collation, null) : StringComparisonOf(type, collation, proved.Strings, reader.National, reader.Converted),
            !reader.Collated && CountsCodePoints(type, collation));
    }

    /// <summary>
    /// Whether <c>LEN</c> of a <paramref name="type"/> column under <paramref name="collation"/> counts a surrogate pair as one
    /// character, so that a minimum it proves is in code points. Observed
    /// (Len_counts_a_surrogate_pair_as_one_only_under_a_supplementary_character_collation): of <c>nchar</c> and <c>nvarchar</c>,
    /// under an <c>_SC</c> collation, or one of version 140 that is not binary (<c>_BIN</c> and <c>_BIN2</c> count two), but not
    /// under <c>_UTF8</c> alone (<c>Latin1_General_100_BIN2_UTF8</c> counts two); of <c>char</c> and <c>varchar</c>, under a
    /// <c>_UTF8</c> collation, binary too, and under no other (a code page has no such character). Under anything else, or when the
    /// collation or type is not known, it counts UTF-16 code units, which a minimum then counts, and which hold under either.
    /// </summary>
    private static bool CountsCodePoints(ResolvedType type, string? collation)
    {
        if (collation is null || !CollationCatalogue.IsKnown(collation) || type.Scalar is not { IsString: true } scalar)
            return false;
        var parts = collation.Split('_');
        bool Has(string part) => parts.Any(t => t.Equals(part, StringComparison.OrdinalIgnoreCase));
        return scalar.IsNational
            ? Has("SC") || (Has("140") && !Has("BIN") && !Has("BIN2"))
            : Has("UTF8");
    }

    /// <summary>
    /// How a column's strings compare with a CHECK's: exactly but for trailing spaces under a binary collation
    /// (<c>_BIN</c>, <c>_BIN2</c>), which compares code points (or bytes) and, like every comparison in SQL Server,
    /// ignores trailing spaces; otherwise under the collation, which may also ignore case, accents, width and more, and
    /// for a code-page column of a collation whose fold is proven, canonically (<see cref="CanonicalBy"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A character may be converted before it is compared, and a character the target lacks becomes another (often <c>?</c>), so
    /// a stored value could differ from the literal. A literal without <c>N</c> is <c>varchar</c> in the database's code page,
    /// whatever the column (observed: A_literal_without_n_is_converted_to_the_databases_code_page_before_it_is_compared), and
    /// for <c>char</c> and <c>varchar</c> in a code page every literal is converted to the column's. Only printable ASCII is the
    /// same in every code page SQL Server uses (<see cref="IsPrintableAscii"/>). A set with anything else, in a literal without
    /// <c>N</c> or under a code-page column, stays a collation set; an <c>N'…'</c> literal compared with a Unicode or UTF-8
    /// column is compared as written.
    /// </para>
    /// <para>
    /// An <c>N'…'</c> literal outranks <c>varchar</c>, so a code-page column compared with one is converted to Unicode and
    /// compared by the collation's Unicode rules, which are not the code-page rules a fold records: under
    /// <c>SQL_Latin1_General_CP1_CI_AS</c>, <c>'v²'</c> equals <c>N'v2'</c> and <c>'paß'</c> equals <c>N'pass'</c>
    /// (observed: A_national_literal_compares_a_code_page_column_by_unicode_rules). Such a set gets no fold. Under a binary
    /// collation, code points are compared, and printable ASCII is the same code point in either, so it stays exact.
    /// </para>
    /// </remarks>
    private static (ValueComparison Comparison, CollationFold? Fold) StringComparisonOf(ResolvedType type, string? collation, IReadOnlyList<string>? strings, bool national, bool converted)
    {
        // A name no collation has says nothing of how it compares, not even whether it is binary.
        if (collation is null || converted || !CollationCatalogue.IsKnown(collation))
            return (ValueComparison.Collation, null);
        var codePage = type.Scalar is { IsString: true, IsNational: false } && SchemaRules.EncodingOf(collation) != TextEncoding.Utf8;
        if (!collation.Split('_').Any(t => t.Equals("BIN", StringComparison.OrdinalIgnoreCase) || t.Equals("BIN2", StringComparison.OrdinalIgnoreCase)))
            return codePage && !national && strings is not null && CanonicalBy(collation, strings) is { } fold ? (ValueComparison.Canonical, fold) : (ValueComparison.Collation, null);
        return codePage && strings is not null && !strings.All(IsPrintableAscii)
            ? (ValueComparison.Collation, null)
            : (ValueComparison.Padded, null);
    }

    /// <summary>
    /// Whether a string is the same in every code page SQL Server uses: printable ASCII, without <c>\</c> and <c>~</c> (code page
    /// 932 has the yen sign at 0x5C).
    /// </summary>
    private static bool IsPrintableAscii(string value) => !value.Any(c => c is < ' ' or > '}' or '\\');

    /// <summary>
    /// The fold a set of a code-page column compares by, when the collation's is proven, every string is plain, and no two fold
    /// alike (which would leave a value two strings to map to); null otherwise, and the set compares under the collation.
    /// </summary>
    private static CollationFold? CanonicalBy(string collation, IReadOnlyList<string> strings) =>
        CollationFold.For(collation, StringKind.CodePage) is { } fold
        && strings.All(CollationFold.IsPlain)
        && strings.Select(fold.Fold).Distinct(StringComparer.Ordinal).Count() == strings.Count
            ? fold
            : null;

    private static bool Same(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase);

    /// <summary>How the column's values compare: as exact numbers of a precision and scale, or as strings. Null for any other type.</summary>
    private static Family? FamilyOf(ResolvedType type) => type.Scalar switch
    {
        { IsInteger: true, Type.Digits: var (precision, scale) } => new Family(false, precision, scale),
        { IsDecimal: true } exact => new Family(false, exact.Precision, exact.Scale),
        { IsMoney: true } money => new Family(false, money.Precision, money.Scale),
        { IsString: true } => new Family(true, 0, 0),
        _ => null,
    };

    private sealed record Family(bool IsString, int Precision, int Scale);

    /// <summary>What a condition proves; every part optional.</summary>
    /// <param name="Numbers">A set of numbers the value is one of.</param>
    /// <param name="Strings">A set of strings the value compares equal to one of.</param>
    /// <param name="Min">A lower bound.</param>
    /// <param name="Max">An upper bound.</param>
    /// <param name="MinLength">A minimum length, in characters as <c>LEN</c> counts them.</param>
    /// <param name="PassesNoValue">
    /// Whether the condition is FALSE for every non-null value, as <c>col IS NULL</c> is; then nothing else is set.
    /// </param>
    private sealed record Proved(
        EquatableArray<ExactDecimal>? Numbers = null,
        EquatableArray<string>? Strings = null,
        Bound? Min = null,
        Bound? Max = null,
        int? MinLength = null,
        bool PassesNoValue = false)
    {
        /// <summary>A condition FALSE for every non-null value (<c>col IS NULL</c>).</summary>
        public static Proved NoNonNullValue { get; } = new(PassesNoValue: true);

        public bool IsValueSet => (Numbers is not null || Strings is not null) && Min is null && Max is null && MinLength is null;

        public ColumnFacts ToFacts(ResolvedType type, FactSource from, (ValueComparison Comparison, CollationFold? Fold) strings, bool codePoints) => new(
            type,
            MinLength: MinLength is { } length ? new MinLengthFact(length, from) { InCodePoints = codePoints } : null,
            Min: Min is { } min ? new BoundFact(min.Value, min.Inclusive, from) : null,
            Max: Max is { } max ? new BoundFact(max.Value, max.Inclusive, from) : null,
            Values: Numbers is not null || Strings is not null
                ? new ValueSetFact(
                    Numbers,
                    strings.Fold is not null && Strings is { } ordered ? [.. ordered.Order(StringComparer.Ordinal)] : Strings,
                    Strings is not null ? strings.Comparison : ValueComparison.Exact,
                    [from],
                    Strings is not null ? strings.Fold : null)
                : null);
    }

    private sealed class Reader(string column, Family family, FactSource source)
    {
        public FactSource Source { get; } = source;

        public Proved? Condition(BooleanExpression expression)
        {
            return expression switch
            {
                BooleanParenthesisExpression parenthesis => Condition(parenthesis.Expression),
                BooleanBinaryExpression { BinaryExpressionType: BooleanBinaryExpressionType.And } and => And(Condition(and.FirstExpression), Condition(and.SecondExpression)),
                BooleanBinaryExpression { BinaryExpressionType: BooleanBinaryExpressionType.Or } or => Or(Condition(or.FirstExpression), Condition(or.SecondExpression)),
                InPredicate { NotDefined: false, Subquery: null } inList when IsColumn(inList.Expression) => ValueSet(inList.Values),
                BooleanTernaryExpression { TernaryExpressionType: BooleanTernaryExpressionType.Between } between
                    when !family.IsString && IsColumn(between.FirstExpression)
                    && Number(between.SecondExpression) is { } low && Number(between.ThirdExpression) is { } high => new Proved(Min: new Bound(low, true), Max: new Bound(high, true)),
                BooleanComparisonExpression comparison => Comparison(comparison),
                BooleanIsNullExpression { IsNot: false } isNull when IsColumn(isNull.Expression) => Proved.NoNonNullValue,
                _ => null,
            };
        }

        private static Proved? And(Proved? a, Proved? b)
        {
            // FALSE AND anything is FALSE: no non-null value passes either way.
            if (a is { PassesNoValue: true } || b is { PassesNoValue: true })
                return Proved.NoNonNullValue;
            if (a is null || b is null)
                return a ?? b;
            return new Proved(
                Numbers: a.Numbers is null || b.Numbers is null ? a.Numbers ?? b.Numbers : [.. a.Numbers.Where(b.Numbers.Contains)],
                Strings: a.Strings ?? b.Strings, // see SchemaRules.Intersect: strings under a collation aren't intersected
                Min: Bounds.Tighter(a.Min, b.Min, BoundSide.Lower),
                Max: Bounds.Tighter(a.Max, b.Max, BoundSide.Upper),
                MinLength: a.MinLength is null || b.MinLength is null ? a.MinLength ?? b.MinLength : Math.Max(a.MinLength.Value, b.MinLength.Value));
        }

        /// <summary>
        /// A side FALSE for every non-null value (<c>col IS NULL</c>) is never the TRUE one, so the other side alone decides, whatever
        /// it proves. Otherwise only a union of value sets is read: anything else on one side could be the TRUE one.
        /// </summary>
        private static Proved? Or(Proved? a, Proved? b)
        {
            if (a is { PassesNoValue: true })
                return b;
            if (b is { PassesNoValue: true })
                return a;
            if (a is not { IsValueSet: true } || b is not { IsValueSet: true })
                return null;
            return a.Numbers is not null && b.Numbers is not null ? new Proved(Numbers: [.. a.Numbers.Union(b.Numbers)])
                : a.Strings is not null && b.Strings is not null ? new Proved(Strings: [.. a.Strings.Union(b.Strings, StringComparer.Ordinal)])
                : null;
        }

        private Proved? ValueSet(IList<ScalarExpression> values)
        {
            if (family.IsString)
            {
                // SQL Server pads the shorter string with spaces before comparing, so a literal's own trailing spaces
                // mean nothing: 'a' passes IN ('a '). They are removed, which makes 'b' and 'b  ' one item.
                var strings = values.Select(String).ToList();
                return strings.Any(s => s is null) ? null : new Proved(Strings: [.. strings.OfType<string>().Select(s => s.TrimEnd(' ')).Distinct(StringComparer.Ordinal)]);
            }

            var numbers = values.Select(Number).ToList();
            return numbers.Any(n => n is null) ? null : new Proved(Numbers: [.. numbers.OfType<ExactDecimal>().Distinct()]);
        }

        private Proved? Comparison(BooleanComparisonExpression comparison)
        {
            var type = comparison.ComparisonType;
            ScalarExpression other;
            if (IsColumn(comparison.FirstExpression))
            {
                other = comparison.SecondExpression;
            }
            else if (IsColumn(comparison.SecondExpression))
            {
                other = comparison.FirstExpression;
                type = Reversed(type);
            }
            else
            {
                return LengthComparison(comparison);
            }

            if (type == BooleanComparisonType.Equals)
                return ValueSet([other]);

            if (family.IsString)
            {
                // col <> '' (and so not all spaces either, which compare equal to it): at least one character.
                return type is BooleanComparisonType.NotEqualToBrackets or BooleanComparisonType.NotEqualToExclamation
                    && String(other) is "" ? new Proved(MinLength: 1) : null;
            }

            if (Number(other) is not { } bound)
                return null;
            return type switch
            {
                BooleanComparisonType.GreaterThan => new Proved(Min: new Bound(bound, false)),
                BooleanComparisonType.GreaterThanOrEqualTo => new Proved(Min: new Bound(bound, true)),
                BooleanComparisonType.LessThan => new Proved(Max: new Bound(bound, false)),
                BooleanComparisonType.LessThanOrEqualTo => new Proved(Max: new Bound(bound, true)),
                _ => null,
            };
        }

        /// <summary><c>len(col) &gt; n</c>, <c>&gt;= n</c> or <c>&lt;&gt; 0</c>, either way round.</summary>
        private Proved? LengthComparison(BooleanComparisonExpression comparison)
        {
            if (!family.IsString)
                return null;

            var type = comparison.ComparisonType;
            ScalarExpression other;
            if (IsLen(comparison.FirstExpression))
            {
                other = comparison.SecondExpression;
            }
            else if (IsLen(comparison.SecondExpression))
            {
                other = comparison.FirstExpression;
                type = Reversed(type);
            }
            else
            {
                return null;
            }

            if (Literal(other) is not IntegerLiteral literal
                || !int.TryParse(literal.Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n))
            {
                return null;
            }

            int? min = type switch
            {
                BooleanComparisonType.GreaterThan => n + 1,
                BooleanComparisonType.GreaterThanOrEqualTo => n,
                BooleanComparisonType.NotEqualToBrackets or BooleanComparisonType.NotEqualToExclamation when n == 0 => 1,
                _ => null,
            };
            return min is > 0 ? new Proved(MinLength: min) : null;
        }

        private bool IsLen(ScalarExpression expression) =>
            Unwrap(expression) is FunctionCall { CallTarget: null, Parameters.Count: 1 } call
            && BuiltInFunctions.Named(call.FunctionName.Value, FunctionForm.Scalar) == ScalarFunctions.Len
            && IsColumn(call.Parameters[0]);

        /// <summary>Whether a column or literal the condition compares carries a <c>COLLATE</c> of its own.</summary>
        public bool Collated { get; private set; }

        /// <summary>Whether a string the condition compares is an <c>N'…'</c> literal (see <see cref="StringComparisonOf"/>).</summary>
        public bool National { get; private set; }

        /// <summary>
        /// Whether a string the condition compares is a literal without <c>N</c> holding a character outside printable ASCII,
        /// which is converted to the database's code page, which the model doesn't know, before it is compared.
        /// </summary>
        public bool Converted { get; private set; }

        private bool IsColumn(ScalarExpression expression)
        {
            var isColumn = Unwrap(expression) is ColumnReferenceExpression { ColumnType: ColumnType.Regular, MultiPartIdentifier.Identifiers: [var name] }
                && Same(name.Value, column);
            Collated |= isColumn && HasCollation(expression);
            return isColumn;
        }

        /// <summary>Whether the expression, or any parenthesis around it, has a <c>COLLATE</c>.</summary>
        private static bool HasCollation(ScalarExpression expression)
        {
            for (var e = expression; ; e = ((ParenthesisExpression)e).Expression)
            {
                if (e is PrimaryExpression { Collation: not null })
                    return true;
                if (e is not ParenthesisExpression)
                    return false;
            }
        }

        /// <summary>A numeric literal the column compares with exactly (see the class remarks), or null.</summary>
        private ExactDecimal? Number(ScalarExpression expression)
        {
            if (family.IsString)
                return null;

            var negative = false;
            var e = Unwrap(expression);
            while (e is UnaryExpression { UnaryExpressionType: UnaryExpressionType.Negative or UnaryExpressionType.Positive } unary)
            {
                negative ^= unary.UnaryExpressionType == UnaryExpressionType.Negative;
                e = Unwrap(unary.Expression);
            }

            // Integer and numeric literals only: a real literal (1e5) is a float, and a money literal ($5) is money,
            // each with its own conversions.
            if (e is not (IntegerLiteral or NumericLiteral) || !ExactDecimal.TryParse(((Literal)e).Value, out var value))
                return null;
            if (value.Scale > family.Scale || value.IntegerDigits > family.Precision - family.Scale)
                return null;
            return negative ? value.Negate() : value;
        }

        private string? String(ScalarExpression expression)
        {
            if (!family.IsString || Literal(expression) is not StringLiteral literal)
                return null;
            Collated |= HasCollation(expression);
            National |= literal.IsNational;
            Converted |= !literal.IsNational && !IsPrintableAscii(literal.Value);
            return literal.Value;
        }

        /// <summary>
        /// A literal, through parentheses; null for anything else, and for one holding a SQLCMD variable, which a deployment replaces
        /// with its value, so that a set or a bound built from it would be the written text's, not what the server enforces
        /// (decision 0046; observed in SqlcmdVariableObservationTests).
        /// </summary>
        private static Literal? Literal(ScalarExpression expression) =>
            Unwrap(expression) is Literal literal && !SqlcmdVariableReference.IsIn(literal) ? literal : null;

        private static ScalarExpression Unwrap(ScalarExpression expression)
        {
            while (expression is ParenthesisExpression parenthesis)
                expression = parenthesis.Expression;
            return expression;
        }

        private static BooleanComparisonType Reversed(BooleanComparisonType type) => type switch
        {
            BooleanComparisonType.GreaterThan => BooleanComparisonType.LessThan,
            BooleanComparisonType.GreaterThanOrEqualTo => BooleanComparisonType.LessThanOrEqualTo,
            BooleanComparisonType.LessThan => BooleanComparisonType.GreaterThan,
            BooleanComparisonType.LessThanOrEqualTo => BooleanComparisonType.GreaterThanOrEqualTo,
            _ => type,
        };
    }

    /// <summary>The name of every column the condition refers to; null for a reference of more than one part.</summary>
    private sealed class ColumnReferences : TSqlFragmentVisitor
    {
        public List<string?> Names { get; } = [];

        public override void Visit(ColumnReferenceExpression node) =>
            Names.Add(node.MultiPartIdentifier?.Identifiers is [var name] ? name.Value : null);
    }
}
