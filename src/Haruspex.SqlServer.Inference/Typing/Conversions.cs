using System.Globalization;
using Haruspex.SqlServer.Semantics;

namespace Haruspex.SqlServer.Inference.Typing;

/// <summary>
/// The server's rules for converting a value to another type: whether every value converts without an overflow, whether
/// it keeps the facts proved of it, and whether a literal fits. README.md in the project ("Conversions") gives the rules
/// and the observations they rest on.
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

    /// <summary>
    /// Whether every value of <paramref name="operand"/> converts to <paramref name="to"/> without an overflow, which is an error by
    /// default and NULL under a caller's ANSI_WARNINGS and ARITHABORT off (observed:
    /// <c>An_overflow_is_an_error_by_default_and_null_under_the_callers_ansi_warnings_and_arithabort_off</c>). A conversion that
    /// fails for a value (a string that is not a number) is an error even then
    /// (<c>A_failed_conversion_is_an_error_even_under_ansi_warnings_and_arithabort_off</c>), so only overflow makes NULL. False
    /// when not proved: each case below was observed at its edge (<c>A_conversion_that_cannot_overflow_keeps_every_value</c>).
    /// </summary>
    /// <param name="styled">Whether CONVERT gives a style, which changes a number's characters, so a number's length is not known.</param>
    public static bool Converts(Operand operand, ScalarType to, bool styled = false)
    {
        var from = operand.Type;
        if (from.Text == to.Text && !styled)
            return true;

        if (to.IsString)
        {
            // A string, a date or a time is cut short to fit, as strings are; anything else must fit whole.
            if (from.IsString || from.IsDateOrTime)
                return true;
            if (styled)
                return false;
            // A sign, the digits, a point when there are places, and a 0 before it when there are only places: -0.5.
            var longest = from.IsDecimal
                ? from.Precision + 1 + (from.Scale > 0 ? 1 : 0) + (from.Precision == from.Scale ? 1 : 0)
                : from.Type.LongestText ?? int.MaxValue;
            return to.IsMax ? longest < int.MaxValue : to.Length >= longest;
        }

        if (to.IsInteger)
        {
            if (from.IsInteger || from.IsBit)
                return to.IntegerDigits >= from.IntegerDigits;

            // A decimal is cut to its integer part, which must fit with any sign: tinyint has none.
            return from.IsDecimal && to.Type != BuiltInType.TinyInt && from.Precision - from.Scale <= to.IntegerDigits - 1;
        }

        if (to.IsDecimal)
        {
            if (!from.IsExact)
                return false;

            // Rounding to fewer places can carry into one more integer digit: 99.999 as decimal(4,2) is 100.00.
            var (p, s) = operand.AsDecimal();
            return to.Precision - to.Scale > p - s || (to.Precision - to.Scale == p - s && to.Scale >= s);
        }

        var t = to.Type;
        if (t == BuiltInType.Bit)
            return from.IsExact;
        if (t == BuiltInType.SmallMoney)
            return from.Type.IsAnyOf(BuiltInType.TinyInt, BuiltInType.SmallInt, BuiltInType.Bit, BuiltInType.SmallMoney) || (from.IsDecimal && from.Precision - from.Scale <= 5);
        if (t == BuiltInType.Float)
            return from.IsExact || from.Type.IsAnyOf(BuiltInType.Real, BuiltInType.Float);
        if (t == BuiltInType.Real)
            return from.IsExact || from.Type == BuiltInType.Real;
        if (t == BuiltInType.Money)
            return (from.IsInteger && from.Type != BuiltInType.BigInt) || from.Type.IsAnyOf(BuiltInType.Bit, BuiltInType.SmallMoney) || (from.IsDecimal && from.Precision - from.Scale <= 14);
        if (t == BuiltInType.Date)
            return from.Type.IsAnyOf(BuiltInType.Date, BuiltInType.DateTime, BuiltInType.DateTime2, BuiltInType.SmallDateTime, BuiltInType.DateTimeOffset);

        // A time is that time on 1 January 1900, rounding into the 2nd at most; a tinyint or bit that many days from 1900,
        // a smallint too within datetime's range (1753 to 9999) but not smalldatetime's (1900 to 2079); a date before
        // 1753 or 1900 overflows (observed: A_conversion_that_cannot_overflow_keeps_every_value_at_its_edges,
        // A_conversion_that_overflows_is_an_error_by_default_and_null_under_the_callers_options).
        if (t == BuiltInType.SmallDateTime)
            return from.Type.IsAnyOf(BuiltInType.SmallDateTime, BuiltInType.Time, BuiltInType.TinyInt, BuiltInType.Bit);
        if (t == BuiltInType.DateTime2)
        {
            return from.Type.IsAnyOf(BuiltInType.Date, BuiltInType.SmallDateTime) || (from.Type == BuiltInType.DateTime && to.Precision >= 3)
                || (from.Type.IsAnyOf(BuiltInType.DateTime2, BuiltInType.Time) && to.Precision >= from.Precision);
        }

        if (t == BuiltInType.DateTimeOffset)
        {
            return from.Type.IsAnyOf(BuiltInType.Date, BuiltInType.SmallDateTime) || (from.Type == BuiltInType.DateTime && to.Precision >= 3)
                || (from.Type.IsAnyOf(BuiltInType.DateTime2, BuiltInType.Time, BuiltInType.DateTimeOffset) && to.Precision >= from.Precision);
        }

        if (t == BuiltInType.DateTime)
            return from.Type.IsAnyOf(BuiltInType.DateTime, BuiltInType.SmallDateTime, BuiltInType.Time, BuiltInType.TinyInt, BuiltInType.SmallInt, BuiltInType.Bit);
        return t == BuiltInType.Time && from.Type == BuiltInType.Time && to.Precision >= from.Precision;
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
