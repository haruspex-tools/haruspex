using System.Globalization;
using Haruspex.SqlServer.Semantics;

namespace Haruspex.SqlServer.Inference.Typing;

/// <summary>
/// The server's rules for converting a value to another type: whether every value converts without an overflow, whether
/// it keeps the facts proved of it, and whether a literal fits. README.md in the project ("Conversions") gives the rules
/// and the observations they rest on, and the guide's chapter <c>docs/internals/08-the-sql-server-type-system-as-data.md</c>
/// why they are a table.
/// </summary>
internal static class Conversions
{
    /// <summary>
    /// Whether every value of <paramref name="operand"/> is the same value in <paramref name="to"/>, so the facts proved of it
    /// (bounds, lengths, a closed set) still hold once converted: an exact number into one holding all its integer digits and
    /// places, a string into a varying string as long, of the same kind. Not a float (whose digits are approximate), not a
    /// rounded decimal, not a string padded or cut short.
    /// </summary>
    public static bool Preserves(Operand operand, ScalarType to)
    {
        var from = operand.Type;
        if (from.Text == to.Text)
            return true;
        if (from.IsString && to.IsString)
            return from.IsNational == to.IsNational && to.IsVarying && (to.IsMax || (!from.IsMax && to.Length >= from.Length));
        if (!(from.IsInteger || from.IsDecimal || from.IsMoney) || !Converts(operand, to))
            return false;
        if (to.IsInteger || to.IsMoney)
            return from.IsInteger || (from.Type == BuiltInType.SmallMoney && to.Type == BuiltInType.Money);
        if (!to.IsDecimal)
            return false;
        var (p, s) = operand.AsDecimal();
        return to.Precision - to.Scale >= p - s && to.Scale >= s;
    }

    private static readonly BuiltInType[] Integers = [BuiltInType.TinyInt, BuiltInType.SmallInt, BuiltInType.Int, BuiltInType.BigInt];

    private static readonly BuiltInType[] Decimals = [BuiltInType.Decimal, BuiltInType.Numeric];

    private static readonly BuiltInType[] Exact = [BuiltInType.Bit, .. Integers, .. Decimals, BuiltInType.Money, BuiltInType.SmallMoney];

    private static readonly BuiltInType[] Strings = [BuiltInType.Char, BuiltInType.VarChar, BuiltInType.NChar, BuiltInType.NVarChar];

    private static readonly BuiltInType[] DatesAndTimes =
        [BuiltInType.Date, BuiltInType.Time, BuiltInType.DateTime, BuiltInType.SmallDateTime, BuiltInType.DateTime2, BuiltInType.DateTimeOffset];

    /// <summary>The observation test that converts each row's types at their edges.</summary>
    private const string AtTheEdges = "Each_conversion_the_table_lists_keeps_every_value_at_the_edges_of_its_types";

    /// <summary>
    /// The observation that a date or time rounded to fewer fractional digits never overflows: at the type's greatest value the
    /// server keeps it.
    /// </summary>
    private const string Rounded = "A_date_or_time_rounded_to_fewer_fractional_digits_never_overflows_since_the_greatest_is_kept";

    /// <summary>
    /// The conversions that keep every value, without an overflow, for the types inference models: a pair not listed may
    /// overflow, or is not modelled, which is the safe answer. Microsoft's conversion chart ("CAST and CONVERT
    /// (Transact-SQL)") says which conversions are allowed; this says which of them can't overflow, a fact the chart doesn't
    /// give, so each row is observed (<see cref="ConversionRow.ObservedIn"/>). A row's dates are about ranges: a time is that
    /// time on 1 January 1900; a tinyint or bit that many days from 1900, and a smallint too within datetime's range (1753
    /// to 9999) but not smalldatetime's (1900 to 2079); and a date before 1753 or 1900 overflows. Rounded to fewer fractional
    /// digits, a date or time never overflows: at the greatest value the server keeps it.
    /// </summary>
    public static IReadOnlyList<ConversionRow> Table { get; } =
    [
        // A string, a date or a time is cut short to fit, as strings are; a number or a GUID must fit whole.
        new([.. Strings], [.. Strings, .. DatesAndTimes], ConversionCondition.Always, [AtTheEdges]),
        new([.. Strings], [BuiltInType.Bit, .. Integers, .. Decimals, BuiltInType.UniqueIdentifier], ConversionCondition.FitsAsText, [AtTheEdges]),
        new([.. Integers], [BuiltInType.Bit, .. Integers], ConversionCondition.WiderInteger, [AtTheEdges]),
        new([BuiltInType.SmallInt, BuiltInType.Int, BuiltInType.BigInt, BuiltInType.SmallMoney, BuiltInType.Money], [.. Decimals], ConversionCondition.IntegerPartFits, [AtTheEdges]),
        new([.. Decimals], [.. Exact], ConversionCondition.DecimalDigitsFit, [AtTheEdges]),
        new([BuiltInType.Bit], [.. Exact], ConversionCondition.Always, [AtTheEdges]),
        new([BuiltInType.SmallMoney], [BuiltInType.Bit, BuiltInType.TinyInt, BuiltInType.SmallInt, BuiltInType.SmallMoney], ConversionCondition.Always, [AtTheEdges]),
        new([BuiltInType.Money], [BuiltInType.Bit, BuiltInType.TinyInt, BuiltInType.SmallInt, BuiltInType.Int, BuiltInType.SmallMoney], ConversionCondition.Always, [AtTheEdges]),
        new([BuiltInType.Float], [.. Exact, BuiltInType.Real, BuiltInType.Float], ConversionCondition.Always, [AtTheEdges]),
        new([BuiltInType.Real], [.. Exact, BuiltInType.Real], ConversionCondition.Always, [AtTheEdges]),
        new([BuiltInType.Date], [BuiltInType.Date, BuiltInType.DateTime, BuiltInType.SmallDateTime, BuiltInType.DateTime2, BuiltInType.DateTimeOffset], ConversionCondition.Always, [AtTheEdges]),
        new([BuiltInType.SmallDateTime], [BuiltInType.Bit, BuiltInType.TinyInt, BuiltInType.Time, BuiltInType.SmallDateTime], ConversionCondition.Always, [AtTheEdges]),
        new([BuiltInType.DateTime], [BuiltInType.Bit, BuiltInType.TinyInt, BuiltInType.SmallInt, BuiltInType.Time, BuiltInType.DateTime, BuiltInType.SmallDateTime], ConversionCondition.Always, [AtTheEdges]),
        new([BuiltInType.DateTime2], [BuiltInType.Date, BuiltInType.Time, BuiltInType.DateTime, BuiltInType.SmallDateTime, BuiltInType.DateTime2], ConversionCondition.Always, [Rounded, AtTheEdges]),
        new([BuiltInType.DateTimeOffset], [.. DatesAndTimes], ConversionCondition.Always, [Rounded, AtTheEdges]),
        new([BuiltInType.Time], [BuiltInType.Time], ConversionCondition.Always, [Rounded, AtTheEdges]),
    ];

    /// <summary>
    /// Whether every value of <paramref name="operand"/> converts to <paramref name="to"/> without an overflow, which is an error by
    /// default and NULL under a caller's ANSI_WARNINGS and ARITHABORT off (observed:
    /// <c>An_overflow_is_an_error_by_default_and_null_under_the_callers_ansi_warnings_and_arithabort_off</c>). A conversion that
    /// fails for a value (a string that is not a number) is an error even then
    /// (<c>A_failed_conversion_is_an_error_even_under_ansi_warnings_and_arithabort_off</c>), so only overflow makes NULL. A
    /// conversion to the same type keeps every value; any other is the <see cref="Table"/>'s row for the two types, and false
    /// when there is none.
    /// </summary>
    /// <param name="styled">Whether CONVERT gives a style, which changes a number's characters, so a number's length is not known.</param>
    public static bool Converts(Operand operand, ScalarType to, bool styled = false)
    {
        var from = operand.Type;
        if (from.Text == to.Text && !styled)
            return true;
        if (Table.FirstOrDefault(r => r.To.Contains(to.Type) && r.From.Contains(from.Type)) is not { } row)
            return false;
        return row.When switch
        {
            ConversionCondition.Always => true,
            ConversionCondition.FitsAsText => !styled && (to.IsMax || to.Length >= LongestText(from)),
            ConversionCondition.WiderInteger => to.IntegerDigits >= from.IntegerDigits,
            ConversionCondition.IntegerPartFits => from.Precision - from.Scale <= to.IntegerDigits - 1,
            ConversionCondition.DecimalDigitsFit => DecimalDigitsFit(operand, to),
            _ => false,
        };
    }

    /// <summary>
    /// The characters the longest value of a type takes as text: a decimal's sign, digits, a point when there are places, and
    /// a 0 before it when there are only places (-0.5); any other type's from the catalogue.
    /// </summary>
    private static int LongestText(ScalarType type) => type.IsDecimal
        ? type.Precision + 1 + (type.Scale > 0 ? 1 : 0) + (type.Precision == type.Scale ? 1 : 0)
        : type.Type.LongestText ?? int.MaxValue;

    /// <summary>Rounding to fewer places can carry into one more integer digit: 99.999 as decimal(4,2) is 100.00.</summary>
    private static bool DecimalDigitsFit(Operand operand, ScalarType to)
    {
        var (p, s) = operand.AsDecimal();
        return to.Precision - to.Scale > p - s || (to.Precision - to.Scale == p - s && to.Scale >= s);
    }

    /// <summary>
    /// Whether converting a value of <paramref name="from"/> to <paramref name="to"/> can never give NULL for a value that is
    /// not: <see cref="Converts"/>, or a string into uniqueidentifier, which is the text's first 36 characters or fails, an error
    /// under any options (observed: A_string_that_is_not_a_value_of_the_type_is_an_error_even_under_the_callers_options). Not
    /// in <see cref="Converts"/>, which also says TRY_CAST cannot fail.
    /// </summary>
    public static bool NeverNull(Operand from, ScalarType to) => Converts(from, to) || (from.Type.IsString && to.Type == BuiltInType.UniqueIdentifier);

    /// <summary>
    /// Whether a literal's value converts to <paramref name="to"/> without an overflow: an integer within its range, or within
    /// a decimal's integer digits; a decimal literal rounded to the target's places; a string into a string. Null when not
    /// decided here, so the type's rule applies.
    /// </summary>
    public static bool? Fits(Microsoft.SqlServer.TransactSql.ScriptDom.Literal literal, ScalarType to, bool negative)
    {
        if (literal is Microsoft.SqlServer.TransactSql.ScriptDom.StringLiteral)
            return to.IsString ? true : null;
        if (literal is not (Microsoft.SqlServer.TransactSql.ScriptDom.IntegerLiteral or Microsoft.SqlServer.TransactSql.ScriptDom.NumericLiteral)
            || !decimal.TryParse(literal.Value, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        if (negative)
            value = -value;

        if (to.IsInteger)
        {
            var whole = ExactDecimal.Parse(decimal.Truncate(value).ToString(CultureInfo.InvariantCulture));
            return whole >= to.Type.Least!.Value && whole <= to.Type.Greatest!.Value;
        }

        if (to.IsDecimal)
        {
            var rounded = decimal.Round(value, Math.Min(to.Scale, 28), MidpointRounding.AwayFromZero);
            var integer = decimal.Truncate(decimal.Abs(rounded));
            var digits = integer == 0 ? 0 : integer.ToString(CultureInfo.InvariantCulture).Length;
            return digits <= to.Precision - to.Scale;
        }

        return null;
    }
}
