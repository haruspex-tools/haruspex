using Haruspex.SqlServer.Semantics;

namespace Haruspex.SqlServer.Inference.Typing;

/// <summary>
/// The server's rules for <c>+</c>, <c>-</c>, <c>*</c>, <c>/</c> and <c>%</c>: the type of the result, whether it may
/// overflow, and whether it may lose precision. README.md in the project ("Decimal arithmetic", "Precision lost") gives the
/// rules and the observations they rest on.
/// </summary>
internal static class Arithmetic
{
    /// <summary>
    /// The type of <c>l op r</c>, or null when it is not modelled (observed:
    /// <c>Decimal_arithmetic_has_the_documented_precision_and_scale</c>,
    /// <c>Arithmetic_over_float_real_money_and_dates_follows_data_type_precedence</c>,
    /// <c>Decimal_arithmetic_beyond_38_digits_caps_the_precision_and_cuts_the_scale</c>,
    /// <c>Modulo_has_the_documented_precision_and_scale_and_integers_and_money_keep_their_type</c>,
    /// <c>A_bit_beside_a_number_takes_the_numbers_type_and_two_bits_are_an_error</c>; and over a grid, ArithmeticGridTests):
    /// <list type="bullet">
    /// <item>datetime or smalldatetime + or - a number, or each other: datetime if either is, else smalldatetime (datetime2,
    /// date, time and datetimeoffset take no arithmetic: an error);</item>
    /// <item>a bit beside another number is converted to that number's own type, a decimal's precision and scale too; two bits
    /// are an error;</item>
    /// <item>a float with any number: float; a real: real; neither takes <c>%</c> (an error);</item>
    /// <item>money or smallmoney with an integer or each other: money if either is, else smallmoney (even beside bigint);</item>
    /// <item>two integers: the wider;</item>
    /// <item>with a decimal, by <see cref="DecimalResult"/>; numeric when either is.</item>
    /// </list>
    /// </summary>
    public static ScalarType? TypeOf(char op, Operand leftOperand, Operand rightOperand)
    {
        var (left, right) = (leftOperand.Type, rightOperand.Type);
        if (left.Type.IsAnyOf(BuiltInType.DateTime, BuiltInType.SmallDateTime) || right.Type.IsAnyOf(BuiltInType.DateTime, BuiltInType.SmallDateTime))
        {
            static bool Dated(ScalarType t) => t.Type.IsAnyOf(BuiltInType.DateTime, BuiltInType.SmallDateTime, BuiltInType.Bit) || t.IsNumber;
            return op is '+' or '-' && Dated(left) && Dated(right)
                ? ScalarType.Of(left.Type == BuiltInType.DateTime || right.Type == BuiltInType.DateTime ? BuiltInType.DateTime : BuiltInType.SmallDateTime)
                : null;
        }

        if (Operands(leftOperand, rightOperand) is not var (lo, ro))
            return null;
        var (l, r) = (lo.Type, ro.Type);
        if (l.IsApproximate || r.IsApproximate)
            return op == '%' ? null : ScalarType.Of(l.Type == BuiltInType.Float || r.Type == BuiltInType.Float ? BuiltInType.Float : BuiltInType.Real);
        if ((l.IsMoney || r.IsMoney) && !l.IsDecimal && !r.IsDecimal)
            return ScalarType.Of(l.Type == BuiltInType.Money || r.Type == BuiltInType.Money ? BuiltInType.Money : BuiltInType.SmallMoney);
        if (l.IsInteger && r.IsInteger)
            return l.IntegerDigits >= r.IntegerDigits ? l : r;

        var name = l.Type == BuiltInType.Numeric || r.Type == BuiltInType.Numeric ? BuiltInType.Numeric : BuiltInType.Decimal;
        return DecimalResult(op, lo, ro) is var (precision, scale, _, _) ? new ScalarType(name, Precision: precision, Scale: scale) : null;
    }

    /// <summary>
    /// The operands as SQL Server computes with them: a bit beside another number becomes that number's type (observed:
    /// <c>A_bit_beside_a_number_takes_the_numbers_type_and_two_bits_are_an_error</c>, bit * decimal(10,2) is decimal(21,4)); null
    /// unless both are numbers, and for two bits.
    /// </summary>
    private static (Operand Left, Operand Right)? Operands(Operand left, Operand right)
    {
        static bool Numeric(ScalarType t) => t.IsNumber || t.IsBit;
        if (!Numeric(left.Type) || !Numeric(right.Type) || (left.Type.IsBit && right.Type.IsBit))
            return null;
        return (left.Type.IsBit ? right : left, right.Type.IsBit ? left : right);
    }

    /// <summary>
    /// The precision and scale of decimal arithmetic, money as decimal(19,4), smallmoney decimal(10,4), an integer as its digits,
    /// as Microsoft documents it ("Precision, scale, and length", linked from README.md), and the precision and scale before any cap:
    /// <list type="bullet">
    /// <item>+ and - give scale max(s1, s2), precision max(p1 - s1, p2 - s2) + scale + 1;</item>
    /// <item>* gives p1 + p2 + 1, s1 + s2;</item>
    /// <item>/ gives scale max(6, s1 + p2 + 1), precision p1 - s1 + s2 + scale;</item>
    /// <item>% gives scale max(s1, s2), precision min(p1 - s1, p2 - s2) + scale, never past 38.</item>
    /// </list>
    /// Past 38 digits the precision is 38 and the scale is cut to keep the integer digits: for + and -, to
    /// 38 - max(p1 - s1, p2 - s2), with no floor; for * and /, to min(s, max(6, 38 - (p - s))), so never below 6 unless it was
    /// already (observed over 144,500 operand pairs when the rule was written; ArithmeticGridTests keeps a grid of them).
    /// </summary>
    private static (int Precision, int Scale, int UncappedPrecision, int UncappedScale)? DecimalResult(char op, Operand left, Operand right)
    {
        static bool Exact(ScalarType t) => t.IsInteger || t.IsDecimal || t.IsMoney;
        if (!Exact(left.Type) || !Exact(right.Type))
            return null;
        var (p1, s1) = left.AsDecimal();
        var (p2, s2) = right.AsDecimal();
        int precision, scale;
        switch (op)
        {
            case '+' or '-':
                scale = Math.Max(s1, s2);
                precision = Math.Max(p1 - s1, p2 - s2) + scale + 1;
                break;
            case '*':
                precision = p1 + p2 + 1;
                scale = s1 + s2;
                break;
            case '/':
                scale = Math.Max(6, s1 + p2 + 1);
                precision = p1 - s1 + s2 + scale;
                break;
            case '%':
                scale = Math.Max(s1, s2);
                precision = Math.Min(p1 - s1, p2 - s2) + scale;
                break;
            default:
                return null;
        }

        if (precision <= 38)
            return (precision, scale, precision, scale);
        var cut = op is '+' or '-'
            ? Math.Min(scale, 38 - Math.Max(p1 - s1, p2 - s2))
            : Math.Min(scale, Math.Max(6, 38 - (precision - scale)));
        return (38, cut, precision, scale);
    }

    /// <summary>
    /// Whether <c>l op r</c> may overflow its type or divide by zero, which is an error by default and NULL under a caller's
    /// ANSI_WARNINGS and ARITHABORT off (observed:
    /// <c>Decimal_overflow_and_division_by_zero_are_errors_by_default_and_null_under_the_callers_options</c>,
    /// <c>Integer_and_money_division_overflow_only_at_the_least_value_divided_by_minus_one</c>). True unless proved:
    /// <list type="bullet">
    /// <item>integers, money and floats can always overflow, but a division or modulo by a literal of at least 1 neither
    /// divides by zero nor grows (the least int divided by -1 is why the literal must be positive);</item>
    /// <item>decimals cannot overflow within 38 digits, except that a bit beside decimal(p, p) becomes that type, which cannot
    /// hold 1; a division or modulo divides by zero unless the divisor is a literal other than 0. <c>%</c> brings both
    /// operands to its scale max(s1, s2) first, which overflows when max(p1 - s1, p2 - s2) + max(s1, s2) passes 38; its result,
    /// smaller than either operand, cannot. Capped past 38 digits, the result
    /// overflows when the integer digits it may need exceed the 38 - scale it keeps: max(p1 - s1, p2 - s2) + 1 for + and -,
    /// (p1 - s1) + (p2 - s2) for *, p1 - s1 + s2 for / (p1 - s1 by a literal of at least 1). For * and / it may also when they
    /// are equal and the scale was cut, since rounding can carry into one more digit (observed: decimal(38,38) squared rounds
    /// up to 1).</item>
    /// </list>
    /// </summary>
    /// <param name="divisor">The right operand's value, when it is a number literal.</param>
    public static bool MayOverflow(char op, Operand leftOperand, Operand rightOperand, decimal? divisor)
    {
        if (Operands(leftOperand, rightOperand) is not var (lo, ro))
            return true;
        var (left, right, l, r) = (leftOperand.Type, rightOperand.Type, lo.Type, ro.Type);
        var byPositive = op is '/' or '%' && divisor >= 1;
        if (l.IsApproximate || r.IsApproximate || (!l.IsDecimal && !r.IsDecimal))
            return !byPositive;
        if (op is '/' or '%' && divisor is null or 0)
            return true;

        // A bit becomes the decimal's own type, which cannot hold its 1 when every digit is a place (observed:
        // A_bit_converted_to_a_decimal_of_places_only_overflows_when_it_is_1).
        if ((left.IsBit || right.IsBit) && l.IsDecimal && l.Precision == l.Scale)
            return true;

        var (p1, s1) = lo.AsDecimal();
        var (p2, s2) = ro.AsDecimal();

        // % first brings each operand to its scale, keeping its integer digits, which past 38 digits overflows (observed:
        // Modulo_brings_both_operands_to_its_scale_first_which_can_overflow); its result, smaller than either, cannot.
        if (op == '%')
            return Math.Max(p1 - s1, p2 - s2) + Math.Max(s1, s2) > 38;

        // Within 38 digits the precision holds every result, so only a capped one can overflow.
        if (DecimalResult(op, lo, ro) is not var (_, scale, uncappedPrecision, uncappedScale) || uncappedPrecision <= 38)
            return false;

        var needed = op switch
        {
            '+' or '-' => Math.Max(p1 - s1, p2 - s2) + 1,
            '*' => p1 - s1 + (p2 - s2),
            _ => byPositive ? p1 - s1 : p1 - s1 + s2,
        };
        var kept = 38 - scale;
        return needed > kept || (needed == kept && op is '*' or '/' && scale < uncappedScale);
    }

    /// <summary>
    /// Whether <c>l op r</c> may round its result to fewer places than the server counts it as having, which with
    /// NUMERIC_ROUNDABORT on is an error or NULL (<c>SessionOptions.RoundingMayBeNull</c>). Only decimal arithmetic does; an
    /// integer's, money's or a float's rounding the option doesn't see. Observed construct by construct
    /// (<c>A_loss_of_precision_is_null_when_numeric_roundabort_is_on_and_ansi_warnings_and_arithabort_are_off</c>), and over
    /// operand pairs whose values are zero, so that only the types decide, and then the largest values of each, checked
    /// through the whole pipeline (PrecisionLossGridTests); thousands of random pairs gave no exception when the rule was
    /// written:
    /// <list type="bullet">
    /// <item><c>*</c> rounds when its scale is cut past 38 digits (<see cref="DecimalResult"/>), whatever the values, 0 * 0 too;</item>
    /// <item><c>+</c> and <c>-</c> when the scale is cut too, but only for a result whose digits at the full scale pass 38,
    /// which no type can rule out once it is cut: 1 + 0.00000000000000000001 as decimal(38,10) + decimal(38,20) loses its last
    /// digit silently, while 0 + 9999999999999999999999999999.9999999 as decimal(37,14) + decimal(35,7) is NULL;</item>
    /// <item><c>/</c> when its scale is below s1 + (p2 - s2) + 1, the dividend's places and the divisor's integer digits and one
    /// more, whatever the values. A quotient whose scale is not cut is at least s1 + p2 + 1 and never rounds so, though 1 / 3
    /// is inexact; nor does one cut by less than that (decimal(10,0) / decimal(20,10) keeps 18 places of 21);</item>
    /// <item><c>%</c> never: its result is exact.</item>
    /// </list>
    /// </summary>
    public static bool Rounds(char op, Operand leftOperand, Operand rightOperand)
    {
        if (Operands(leftOperand, rightOperand) is not var (lo, ro))
            return false;
        var (l, r) = (lo.Type, ro.Type);
        if (l.IsApproximate || r.IsApproximate || (!l.IsDecimal && !r.IsDecimal))
            return false;
        if (DecimalResult(op, lo, ro) is not var (_, scale, _, uncappedScale))
            return false;
        var (_, s1) = lo.AsDecimal();
        var (p2, s2) = ro.AsDecimal();
        return op switch
        {
            '+' or '-' or '*' => scale < uncappedScale,
            '/' => scale < s1 + (p2 - s2) + 1,
            _ => false,
        };
    }
}
