using Haruspex.Ir;
using Haruspex.SqlServer.Inference.Lattice;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

// AVG, the statistical aggregates and the numeric functions. Decimal arithmetic itself is in ScalarType.cs. README.md
// in this folder ("Decimal arithmetic", "Numeric functions") gives the rules and the observations they rest on
// (NumericObservationTests in the differential tests).
internal sealed partial class ProcedureAnalyzer
{
    private IrNullReason Domain(TSqlFragment at) => new(IrNullRule.DomainError, At(at).ToString());

    // ---- AVG and the statistical aggregates ---------------------------------------------------------------------------

    /// <summary>
    /// The type AVG gives a value of this type, with a window too: int for an integer narrower than bigint, bigint,
    /// decimal(38, max(s, 6)) of the argument's name, money, float; null for a bit, which is an error (observed:
    /// Avg_is_typed_by_its_argument_with_or_without_a_window).
    /// </summary>
    private static ScalarType? AvgType(ScalarType of) => of switch
    {
        { Name: "tinyint" or "smallint" or "int" } => new ScalarType("int"),
        { Name: "bigint" } => new ScalarType("bigint"),
        { IsDecimal: true } d => new ScalarType(d.Name, Precision: 38, Scale: Math.Max(d.Scale, 6)),
        { Name: "money" or "smallmoney" } => new ScalarType("money"),
        { Name: "float" or "real" } => new ScalarType("float"),
        _ => null,
    };

    /// <summary>
    /// Whether AVG or SUM of a value of this type may overflow: both sum in the argument's widened type (two ints overflow AVG's
    /// int; observed: Avg_is_null_over_no_rows_and_overflows_as_sum_does), except a decimal of at most 19 digits, by the argument
    /// <see cref="Sum"/> gives. AVG's scale of at least 6 leaves 32 integer digits, which hold a 19-digit decimal's average; a
    /// decimal of 38 digits overflows it (observed).
    /// </summary>
    private static bool SumMayOverflow(ScalarType of) => of is not { IsDecimal: true, Precision: <= 19 };

    /// <summary>
    /// <c>AVG(x)</c> and <c>STDEV</c>, <c>STDEVP</c>, <c>VAR</c>, <c>VARP</c> as aggregates: NULL over no rows without GROUP BY
    /// (<c>empty-aggregate</c>) and when every value is (the argument's reasons), as SUM is; the sample forms also over a single
    /// value (<c>too-few-values</c>, with GROUP BY too). HAVING comparing one proves it, as for any aggregate. The statistics are
    /// float, and overflow only over a float, whose squares can pass float's range; an exact type's or a real's cannot
    /// (observed: The_statistical_aggregates_overflow_only_over_float).
    /// </summary>
    private Value Average(FunctionCall call, BindingScope scope, string name)
    {
        var argument = InAggregate(() => Expression(call.Parameters[0], scope));
        if (argument.Shape is UnknownShape)
            return argument;
        if (argument.IsLiveJson || TypeOf(argument, call.Parameters[0]) is not { } of || Statistic(name, of) is not var (type, overflows))
            return NotModelled(call, $"{name.ToUpperInvariant()} of a value of this type is not modelled.");
        if (ProvedAggregate(call))
            return Typed(type, [], call);

        EquatableArray<IrNullReason> because = [.. argument.NullableBecause, .. Aggregate(call)];
        if (name is "stdev" or "var")
            because = NullReasons.Join(because, [new IrNullReason(IrNullRule.TooFewValues, At(call).ToString())]);
        return Typed(type, overflows ? NullReasons.Join(because, [Overflow(call)]) : because, call);
    }

    /// <summary>The type of AVG or a statistic of a value of this type, and whether it may overflow; null when not a number.</summary>
    private static (ScalarType Type, bool Overflows)? Statistic(string name, ScalarType of)
    {
        if (name == "avg")
            return AvgType(of) is { } type ? (type, SumMayOverflow(of)) : null;
        return of.IsNumber ? (new ScalarType("float"), of.Name == "float") : null;
    }

    // ---- numeric functions --------------------------------------------------------------------------------------------

    /// <summary>
    /// The type ROUND, ABS, SIGN, FLOOR, CEILING and POWER give an argument of this type, where the decimal is the argument's own
    /// and each function then changes it: tinyint and smallint give int, int and bigint themselves, money and smallmoney money,
    /// float, real and bit float (observed: Round_floor_ceiling_abs_and_sign_are_typed_by_their_argument,
    /// Power_is_typed_by_its_first_argument_and_the_others_are_float).
    /// </summary>
    private static ScalarType? FunctionType(ScalarType of) => of switch
    {
        { Name: "tinyint" or "smallint" or "int" } => new ScalarType("int"),
        { Name: "bigint" } => new ScalarType("bigint"),
        { IsDecimal: true } => of,
        { IsMoney: true } => new ScalarType("money"),
        { IsApproximate: true } or { Name: "bit" } => new ScalarType("float"),
        _ => null,
    };

    /// <summary>The value of a whole-number literal, through parentheses and signs; null for anything else.</summary>
    private static int? WholeOf(ScalarExpression expression) =>
        LiteralOf(expression) is ({ } literal and IntegerLiteral, var negative) && int.TryParse(literal.Value, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var n)
            ? negative ? -n : n
            : null;

    /// <summary>A numeric function's argument: its value, and its type when it is a number or a bit.</summary>
    private (Value Value, ScalarType? Type) NumberArgument(ScalarExpression expression, BindingScope scope)
    {
        var value = Expression(expression, scope);
        return (value, value.Shape is UnknownShape || value.IsLiveJson ? null : TypeOf(value, expression) is { } t && (t.IsNumber || t.Name == "bit") ? t : null);
    }

    private Value Numeric(FunctionCall call, BindingScope scope)
    {
        var p = call.Parameters;
        var (value, type) = NumberArgument(p[0], scope);
        if (value.Shape is UnknownShape)
            return value;
        if (type is null || FunctionType(type) is not { } result)
            return NotModelled(call, "an argument of this type is not modelled.");

        // Arguments after the first: each NULL makes the result NULL, except ROUND's third (observed: a NULL third argument
        // rounds).
        var (others, unknown) = Arguments(p.Skip(1), scope);
        if (unknown is not null)
            return unknown;
        if (others.Select((o, i) => o.IsLiveJson ? null : TypeOf(o, p[i + 1])).Any(t => t is not ({ IsNumber: true } or { Name: "bit" })))
            return NotModelled(call, "an argument of this type is not modelled.");
        var counted = call.FunctionName.Value.Equals("round", StringComparison.OrdinalIgnoreCase) ? others.Take(1) : others;
        var because = NullReasons.Join(value.NullableBecause, Reasons(counted));
        EquatableArray<IrNullReason> With(bool overflows, bool domain = false) =>
            NullReasons.Join(because, [.. overflows ? [Overflow(call)] : (EquatableArray<IrNullReason>)[], .. domain ? [Domain(call)] : (EquatableArray<IrNullReason>)[]]);
        var flt = new ScalarType("float");

#pragma warning disable CA1308 // Built-in function names are ASCII, so lower case matches them exactly, and the cases are written as T-SQL usually is.
        return (call.FunctionName.Value.ToLowerInvariant(), p.Count) switch
#pragma warning restore CA1308
        {
            ("round", 2 or 3) => Typed(result, With(RoundMayOverflow(type, p)), call),

            // FLOOR and CEILING keep a decimal's precision with no places, which always holds the result (CEILING of
            // decimal(2,2) is 1); money at its extremes does not.
            ("floor" or "ceiling", 1) => Typed(result.IsDecimal ? result with { Scale = 0 } : result, With(type.Name == "money"), call),

            // ABS of the least int, bigint or money overflows; tinyint and smallint become int, and a decimal is symmetric.
            ("abs", 1) => Typed(result, With(type.Name is "int" or "bigint" or "money"), call),

            // SIGN gives -1, 0 or 1: a decimal of places only gains an integer digit to hold 1, within 38.
            ("sign", 1) => Typed(result.IsDecimal && result.Precision == result.Scale ? result with { Precision = Math.Min(result.Precision + 1, 38), Scale = Math.Min(result.Scale, 37) } : result, because, call),

            ("power", 2) => Power(call, type, result, because),

            // SQUARE of an exact type or a real is far within float's range (10^76 at most); of a float it can pass it.
            ("square", 1) => Typed(flt, With(type.IsApproximate && type.Name == "float"), call),

            // EXP passes float's range past e^709: an argument below 709 cannot, a tinyint, a bit or a decimal of at most two
            // integer digits.
            ("exp", 1) => Typed(flt, With(!(type.Name is "tinyint" or "bit" || (type.IsDecimal && type.Precision - type.Scale <= 2))), call),

            // SQRT of a negative is outside its domain, which a tinyint or a bit cannot be; LOG and LOG10 of 0 can be.
            ("sqrt", 1) => Typed(flt, With(false, type.Name is not ("tinyint" or "bit")), call),
            ("log", 1 or 2) or ("log10", 1) => Typed(flt, With(false, domain: true), call),
            _ => NotModelled(call, "this form of the function is not modelled."),
        };
    }

    /// <summary>
    /// Whether <c>ROUND(x, n [, f])</c> may overflow: it truncates when f is a literal other than 0, which cannot; otherwise it
    /// may carry into a digit x's type lacks (ROUND(9.5, 0) is 10.0, observed:
    /// Round_overflows_when_it_carries_into_a_digit_its_type_lacks_unless_it_truncates), unless n is a literal leaving every
    /// place x has, or x becomes a wider type (tinyint and smallint int, real and bit float, smallmoney money), or x is money
    /// rounded to 2 places.
    /// </summary>
    private static bool RoundMayOverflow(ScalarType type, IList<ScalarExpression> p)
    {
        if (p.Count == 3 && WholeOf(p[2]) is { } truncate && truncate != 0)
            return false;
        var places = WholeOf(p[1]);
        return type switch
        {
            { Name: "tinyint" or "smallint" or "real" or "bit" or "smallmoney" } => false,
            { IsDecimal: true } d => !(places >= d.Scale),
            // money's ends are ±922337203685477.58xx with .5807 and .5808 below .585, so rounding to 2 places never carries past
            // them; to 3, 1 or 0 places it can (observed).
            { Name: "money" } => places is not (>= 4 or 2),
            _ => !(places >= 0),
        };
    }

    /// <summary>
    /// <c>POWER(x, y)</c>, x's type (a decimal as decimal(38, s)), computed in float: it may overflow its type, divide by zero (0
    /// to a negative power) or be outside its domain (a negative to a fractional power), each NULL under the caller's options
    /// (observed: Power_computes_in_float_and_overflows_or_divides_by_zero_as_its_type_allows). A whole literal exponent n of at
    /// least 0 leaves only the overflow, which cannot happen when x's integer digits n times over stay below what the type holds:
    /// 38 less the scale for a decimal, 10 digits for int, 19 for bigint (strictly, since float rounds the value); a bit's 0 or 1
    /// never does.
    /// </summary>
    private Value Power(FunctionCall call, ScalarType type, ScalarType result, EquatableArray<IrNullReason> because)
    {
        var typed = result.IsDecimal ? result with { Precision = 38 } : result;
        if (WholeOf(call.Parameters[1]) is not { } n)
            return Typed(typed, NullReasons.Join(because, [Overflow(call), Domain(call)]), call);
        if (n < 0)
            return Typed(typed, NullReasons.Join(because, [Overflow(call)]), call);
        var fits = type switch
        {
            { IsDecimal: true } d => (d.Precision - d.Scale) * n < 38 - d.Scale,
            { Name: "tinyint" } => 3 * n < 10,
            { Name: "smallint" } => 5 * n < 10,
            { Name: "int" } => 10 * n < 10,
            { Name: "bigint" } => 19 * n < 19,
            { Name: "bit" } => true,
            _ => false,
        };
        return Typed(typed, fits ? because : NullReasons.Join(because, [Overflow(call)]), call);
    }
}
