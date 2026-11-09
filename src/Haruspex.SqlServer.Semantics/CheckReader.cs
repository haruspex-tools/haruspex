using Haruspex.SqlServer.Model;
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
/// <para><b>What is read.</b> A constraint that refers to this column and no other, trusted as above, whose condition is built from these by AND (each conjunct on its own) and OR (only of
/// value sets):</para>
/// <list type="bullet">
/// <item><c>col IN (literal, …)</c> and <c>col = literal</c>: a value set;</item>
/// <item><c>col BETWEEN a AND b</c> and <c>col &lt; literal</c> (and <c>&lt;=</c>, <c>&gt;</c>, <c>&gt;=</c>, either way round): bounds, for numbers;</item>
/// <item><c>len(col) &gt; n</c>, <c>len(col) &gt;= n</c>, <c>len(col) &lt;&gt; 0</c> and <c>col &lt;&gt; ''</c>: a minimum length, for strings.</item>
/// </list>
/// <para><b>Why each is sound.</b> A CHECK rejects a row only when its condition is FALSE; TRUE and UNKNOWN pass. For
/// a non-null value, each form above is TRUE or FALSE, never UNKNOWN (a NULL among the literals would make
/// <c>IN</c> UNKNOWN, so such a list is not read). So for a non-null value: <c>A AND B</c> passing means neither is
/// FALSE, so each is TRUE, and a conjunct that is not understood can be skipped; <c>A OR B</c> passing means one is
/// TRUE, so it is read only when every side is a value set, and gives their union. Null is never ruled out: it makes
/// every form UNKNOWN, which passes.</para>
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
/// for trailing spaces), and otherwise <see cref="ValueComparison.Collation"/>; see StringComparisonOf.</para>
/// <para><b>Minimum length.</b> <c>LEN</c> does not count trailing spaces, and counts a character (a surrogate pair
/// under an <c>_SC</c> collation, several bytes in UTF-8) as one, so a value's UTF-16 length is at least its
/// <c>LEN</c>. <c>col &lt;&gt; ''</c> is FALSE for an empty string (and for spaces, which compare equal to it), so
/// the value has at least one character.</para>
/// </remarks>
public static class CheckReader
{
    /// <summary>What <paramref name="check"/> proves about <paramref name="column"/>, or null when it proves nothing this reads.</summary>
    /// <returns>Facts with only <see cref="ColumnFacts.Min"/>, <see cref="ColumnFacts.Max"/>, <see cref="ColumnFacts.MinLength"/> and <see cref="ColumnFacts.Values"/> set.</returns>
    public static ColumnFacts? Read(Table table, Column column, ResolvedType type, CheckConstraint check)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(column);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(check);

        // A disabled or NOT FOR REPLICATION constraint does not check every row.
        if (check.DisabledAt is not null || check.IsNotForReplication)
            return null;

        // Only constraints on this column alone.
        if (check.Column is { } declaredOn && !Same(declaredOn, column.Name))
            return null;
        var references = new ColumnReferences();
        check.Expression.Accept(references);
        if (references.Names.Count == 0 || references.Names.Any(n => n is null || !Same(n, column.Name)))
            return null;

        if (FamilyOf(type.BuiltIn) is not { } family)
            return null;

        var reader = new Reader(column.Name, family, new CheckSource(table, check));
        var proved = reader.Condition(check.Expression);
        return proved?.ToFacts(type, reader.Source, StringComparisonOf(type.BuiltIn, column.Collation, proved.Strings));
    }

    /// <summary>
    /// How a column's strings compare with a CHECK's: exactly but for trailing spaces under a binary collation
    /// (<c>_BIN</c>, <c>_BIN2</c>), which compares code points (or bytes) and, like every comparison in SQL Server,
    /// ignores trailing spaces; otherwise under the collation, which may also ignore case, accents, width and more.
    /// </summary>
    /// <remarks>
    /// For <c>char</c> and <c>varchar</c> in a code page, a literal is converted to the code page before comparing,
    /// and a character the code page lacks becomes another (often <c>?</c>), so a stored value could differ from the
    /// literal. Only printable ASCII is the same in every code page SQL Server uses, and even there <c>\</c> and
    /// <c>~</c> are left out (code page 932 has the yen sign at 0x5C). A set with anything else stays a collation
    /// set. UTF-8 and Unicode columns hold every character, so they need no such care.
    /// </remarks>
    private static ValueComparison StringComparisonOf(SqlType type, string? collation, IReadOnlyList<string>? strings)
    {
        if (collation is null || !collation.Split('_').Any(t => t.Equals("BIN", StringComparison.OrdinalIgnoreCase) || t.Equals("BIN2", StringComparison.OrdinalIgnoreCase)))
            return ValueComparison.Collation;
        var codePage = type.Name is "char" or "varchar" && SchemaRules.EncodingOf(collation) != TextEncoding.Utf8;
        return codePage && strings is not null && strings.Any(s => s.Any(c => c is < ' ' or > '}' or '\\'))
            ? ValueComparison.Collation
            : ValueComparison.Padded;
    }

    private static bool Same(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase);

    /// <summary>How the column's values compare: as exact numbers of a precision and scale, or as strings. Null for any other type.</summary>
    private static Family? FamilyOf(SqlType type)
    {
        var arguments = type.Arguments;
        int Arg(int i, int absent) => arguments.Count > i && int.TryParse(arguments[i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : absent;
        return type.Name switch
        {
            "tinyint" => new Family(false, 3, 0),
            "smallint" => new Family(false, 5, 0),
            "int" => new Family(false, 10, 0),
            "bigint" => new Family(false, 19, 0),
            "decimal" or "numeric" when arguments.Count <= 2 => new Family(false, Arg(0, 18), Arg(1, 0)),
            "money" => new Family(false, 19, 4),
            "smallmoney" => new Family(false, 10, 4),
            "char" or "varchar" or "nchar" or "nvarchar" => new Family(true, 0, 0),
            _ => null,
        };
    }

    private sealed record Family(bool IsString, int Precision, int Scale);

    /// <summary>What a condition proves; every part optional.</summary>
    private sealed record Proved(
        IReadOnlyList<ExactDecimal>? Numbers = null,
        IReadOnlyList<string>? Strings = null,
        (ExactDecimal Value, bool Inclusive)? Min = null,
        (ExactDecimal Value, bool Inclusive)? Max = null,
        int? MinLength = null)
    {
        public bool IsValueSet => (Numbers is not null || Strings is not null) && Min is null && Max is null && MinLength is null;

        public ColumnFacts ToFacts(ResolvedType type, FactSource from, ValueComparison strings) => new(
            type,
            MinLength: MinLength is { } length ? new MinLengthFact(length, from) : null,
            Min: Min is { } min ? new BoundFact(min.Value, min.Inclusive, from) : null,
            Max: Max is { } max ? new BoundFact(max.Value, max.Inclusive, from) : null,
            Values: Numbers is not null || Strings is not null
                ? new ValueSetFact(Numbers, Strings, Strings is not null ? strings : ValueComparison.Exact, [from])
                : null);
    }

    private sealed class Reader(string column, Family family, FactSource source)
    {
        public FactSource Source { get; } = source;

        public Proved? Condition(BooleanExpression expression)
        {
            switch (expression)
            {
                case BooleanParenthesisExpression parenthesis:
                    return Condition(parenthesis.Expression);
                case BooleanBinaryExpression { BinaryExpressionType: BooleanBinaryExpressionType.And } and:
                    return And(Condition(and.FirstExpression), Condition(and.SecondExpression));
                case BooleanBinaryExpression { BinaryExpressionType: BooleanBinaryExpressionType.Or } or:
                    return Or(Condition(or.FirstExpression), Condition(or.SecondExpression));
                case InPredicate { NotDefined: false, Subquery: null } inList when IsColumn(inList.Expression):
                    return ValueSet(inList.Values);
                case BooleanTernaryExpression { TernaryExpressionType: BooleanTernaryExpressionType.Between } between
                    when !family.IsString && IsColumn(between.FirstExpression)
                    && Number(between.SecondExpression) is { } low && Number(between.ThirdExpression) is { } high:
                    return new Proved(Min: (low, true), Max: (high, true));
                case BooleanComparisonExpression comparison:
                    return Comparison(comparison);
                default:
                    return null;
            }
        }

        private static Proved? And(Proved? a, Proved? b)
        {
            if (a is null || b is null)
                return a ?? b;
            return new Proved(
                Numbers: a.Numbers is null || b.Numbers is null ? a.Numbers ?? b.Numbers : [.. a.Numbers.Where(b.Numbers.Contains)],
                Strings: a.Strings ?? b.Strings, // see SchemaRules.Intersect: strings under a collation aren't intersected
                Min: Pick(a.Min, b.Min, lower: true),
                Max: Pick(a.Max, b.Max, lower: false),
                MinLength: a.MinLength is null || b.MinLength is null ? a.MinLength ?? b.MinLength : Math.Max(a.MinLength.Value, b.MinLength.Value));
        }

        private static (ExactDecimal, bool)? Pick((ExactDecimal Value, bool Inclusive)? a, (ExactDecimal Value, bool Inclusive)? b, bool lower)
        {
            if (a is not { } x || b is not { } y)
                return a ?? b;
            var order = x.Value.CompareTo(y.Value);
            if (order == 0)
                return x.Inclusive ? y : x;
            return (order > 0) == lower ? x : y;
        }

        /// <summary>Only a union of value sets is read: anything else on one side could be the TRUE one.</summary>
        private static Proved? Or(Proved? a, Proved? b)
        {
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
                var strings = values.Select(String).ToList();
                return strings.Any(s => s is null) ? null : new Proved(Strings: [.. strings.Distinct(StringComparer.Ordinal)!]);
            }

            var numbers = values.Select(Number).ToList();
            return numbers.Any(n => n is null) ? null : new Proved(Numbers: [.. numbers.Select(n => n!.Value).Distinct()]);
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
                    && String(other) == "" ? new Proved(MinLength: 1) : null;
            }

            if (Number(other) is not { } bound)
                return null;
            return type switch
            {
                BooleanComparisonType.GreaterThan => new Proved(Min: (bound, false)),
                BooleanComparisonType.GreaterThanOrEqualTo => new Proved(Min: (bound, true)),
                BooleanComparisonType.LessThan => new Proved(Max: (bound, false)),
                BooleanComparisonType.LessThanOrEqualTo => new Proved(Max: (bound, true)),
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
                return null;

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
            && call.FunctionName.Value.Equals("len", StringComparison.OrdinalIgnoreCase)
            && IsColumn(call.Parameters[0]);

        private bool IsColumn(ScalarExpression expression) =>
            Unwrap(expression) is ColumnReferenceExpression { ColumnType: ColumnType.Regular, MultiPartIdentifier.Identifiers: [var name] }
            && Same(name.Value, column);

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

        private string? String(ScalarExpression expression) => family.IsString && Literal(expression) is StringLiteral s ? s.Value : null;

        private static Literal? Literal(ScalarExpression expression) => Unwrap(expression) as Literal;

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
