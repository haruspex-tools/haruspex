using Haruspex.Collections;
using Haruspex.Ir;
using Haruspex.SqlServer.Inference.Lattice;
using Haruspex.SqlServer.Inference.Typing;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

// The date functions: parts and differences, the from-parts constructors, and rounding, offsets and ISDATE. Types come from
// ScalarType.cs; README.md in this folder ("Dates") gives the rules and the observations they rest on (FunctionObservationTests
// in the differential tests, its Dates part for the rounding, offset and ISDATE functions).
internal sealed partial class Typer
{
    /// <summary>
    /// Whether a value may stand for a date in a date function: a date or time type, or a string or integer, which is
    /// converted to datetime (observed: Date_functions_are_typed_by_their_kind_and_dateadd_by_its_dates_type). A conversion that
    /// fails is an error, not NULL.
    /// </summary>
    private static ScalarType? DateOperand(Value value, ScalarExpression expression) =>
        value.IsLiveJson || Strip(expression) is NullLiteral ? null
            : TypeOf(value) is { } type && (type.IsDateOrTime || type.IsString || type.IsInteger) ? type : null;

    /// <summary>
    /// <c>DATEDIFF(part, a, b)</c> (int) and <c>DATEDIFF_BIG</c> (bigint): NULL when a or b is. Its overflow is an error, under
    /// any session options (observed: A_date_overflow_is_an_error_even_under_the_callers_ansi_warnings_and_arithabort_off), so
    /// no <c>arithmetic-overflow</c>. The part is a keyword, never read as an expression.
    /// </summary>
    private Value DateDiff(FunctionCall call, ScalarType type, ReadScope scope)
    {
        var (values, unknown) = Arguments(call.Parameters.Skip(1), scope);
        if (unknown is not null)
            return unknown;
        var dates = values.Select((v, i) => DateOperand(v, call.Parameters[i + 1])).ToList();
        if (dates.Any(t => t is null))
            return NotModelled(call, "an argument of this type is not modelled as a date.");
        return Typed(type, NullReasons.Join(Reasons(values), DatetimeOverflow([.. dates.OfType<ScalarType>().Select((t, i) => (t, call.Parameters[i + 1]))], call)), call);
    }

    /// <summary>
    /// Whether reading these as dates may overflow: an integer is converted to datetime, and so is every date beside it, and
    /// either may be past datetime's range (1753 to 9999), which is NULL under a caller's ANSI_WARNINGS and ARITHABORT off
    /// (observed: A_date_function_given_an_integer_converts_it_and_any_date_beside_it_to_datetime_which_may_overflow:
    /// YEAR(2147483647), and DATEDIFF of 1 and 0001-01-01). Without an integer the dates are read as they are; a tinyint, a
    /// smallint, a datetime, a smalldatetime or a time always fits (<see cref="Conversions.Converts"/>).
    /// </summary>
    private EquatableArray<IrNullReason> DatetimeOverflow(IReadOnlyList<(ScalarType Type, ScalarExpression Expression)> dates, TSqlFragment call) =>
        dates.Any(d => d.Type.IsInteger) && !dates.All(d => Fits(d.Type, d.Expression))
            ? Overflow(call)
            : [];

    /// <summary>
    /// Whether a date read as datetime fits it: by its type (<see cref="Conversions.Converts"/>), or an integer literal of a
    /// number of days from 1900-01-01 that datetime holds, -53690 (1753-01-01) to 2958463 (9999-12-31), observed in the same test.
    /// </summary>
    private static bool Fits(ScalarType type, ScalarExpression expression) =>
        Conversions.Converts(Operand.OfType(type), ScalarType.Of(BuiltInType.DateTime))
        || (type.IsInteger && NumberOf(expression) is >= -53690 and <= 2958463);

    /// <summary>
    /// <c>DATEADD(part, n, d)</c>: d's type, or datetime for a string or an integer; NULL when n or d is. An overflow is an
    /// error (517) under any session options.
    /// </summary>
    private Value DateAdd(FunctionCall call, ReadScope scope)
    {
        var (values, unknown) = Arguments(call.Parameters.Skip(1), scope);
        if (unknown is not null)
            return unknown;
        // The NULL literal as the number does not compile (error 8116), where a NULL variable gives NULL.
        if (Strip(call.Parameters[1]) is NullLiteral || TypeOf(values[0]) is not { IsNumber: true })
            return NotModelled(call, "a number of this type is not modelled.");
        var date = DateOperand(values[1], call.Parameters[2]);
        if (date is null)
            return NotModelled(call, "a date of this type is not modelled.");
        return Typed(date.IsDateOrTime ? date : ScalarType.Of(BuiltInType.DateTime), NullReasons.Join(Reasons(values), DatetimeOverflow([(date, call.Parameters[2])], call)), call);
    }

    /// <summary><c>DATEPART</c>, <c>YEAR</c>, <c>MONTH</c>, <c>DAY</c> (int) and <c>DATENAME</c> (nvarchar(30), whose names
    /// depend on the session's language, so no closed set): NULL when the date is.</summary>
    private Value DatePart(FunctionCall call, ScalarExpression date, ScalarType type, ReadScope scope)
    {
        var value = Expression(date, scope);
        if (value.Shape is UnknownShape)
            return value;
        if (DateOperand(value, date) is not { } read)
            return NotModelled(call, "a date of this type is not modelled.");
        return Typed(type, NullReasons.Join(value.NullableBecause, DatetimeOverflow([(read, date)], call)), call);
    }

    /// <summary>
    /// <c>EOMONTH(d [, n])</c>: a date, NULL when d is. A NULL n is taken as 0, so it does not make it NULL (observed:
    /// A_date_function_is_null_when_an_argument_is_but_eomonth_ignores_a_null_offset).
    /// </summary>
    private Value EndOfMonth(FunctionCall call, ReadScope scope)
    {
        var date = call.Parameters[0];
        var value = Expression(date, scope);
        if (value.Shape is UnknownShape)
            return value;

        // The offset is converted to int implicitly, which the server refuses for a date or a GUID (206) when the procedure is
        // created (observed: Json_value_eomonth_string_agg_and_percentiles_refuse_arguments_they_cannot_read).
        if (call.Parameters.Count == 2 && TypeOf(Expression(call.Parameters[1], scope)) is { } offset
            && ConversionChart.Of(offset.Type, BuiltInType.Int) != ConversionKind.Implicit)
        {
            return NotModelled(call, $"SQL Server refuses it: its offset, {offset.Text}, doesn't convert implicitly to int.");
        }

        if (DateOperand(value, date) is not { } type || type.IsInteger || type.Type == BuiltInType.Time)
            return NotModelled(call, "a date of this type is not modelled.");
        return Typed(ScalarType.Of(BuiltInType.Date), value.NullableBecause, call);
    }

    /// <summary>
    /// <c>DATEFROMPARTS</c> and its siblings: of their type, with the precision given as the last argument, which must be a
    /// literal; NULL when any part is. Parts out of range are an error (289). Only integer parts narrower than bigint are
    /// read, which convert to int without an overflow.
    /// </summary>
    private Value FromParts(FunctionCall call, ScalarType type, ReadScope scope, bool precision = false)
    {
        var parts = precision ? call.Parameters.Take(call.Parameters.Count - 1).ToList() : [.. call.Parameters];
        if (precision)
        {
            if (Strip(call.Parameters[^1]) is not IntegerLiteral { Value: var digits } || !int.TryParse(digits, out var p) || p > 7)
                return NotModelled(call, "its precision is not a literal from 0 to 7.");
            type = type with { Precision = p };
        }

        var (values, unknown) = Arguments(parts, scope);
        if (unknown is not null)
            return unknown;
        if (values.Select((v, i) => Strip(parts[i]) is NullLiteral || (TypeOf(v) is { IsInteger: true } part && part.Type != BuiltInType.BigInt)).Any(ok => !ok))
            return NotModelled(call, "a part that is not an int is not modelled.");
        return Typed(type, Reasons(values), call);
    }

    /// <summary>
    /// <c>DATETRUNC(part, d)</c> and <c>DATE_BUCKET(part, width, d [, origin])</c> of a date or time type: d's type; NULL when d
    /// or the width is. A NULL origin is taken as the default origin, and an origin must be of d's type; a part the type lacks
    /// and a bucket past the range are errors (observed:
    /// Datetrunc_and_date_bucket_keep_the_dates_type_and_are_null_only_for_a_null_date_or_width).
    /// </summary>
    private Value DateRounded(FunctionCall call, ReadScope scope, bool bucket)
    {
        var date = call.Parameters[bucket ? 2 : 1];
        var (values, unknown) = Arguments(bucket ? [call.Parameters[1], date] : [date], scope);
        if (unknown is not null)
            return unknown;
        if (TypeOf(values[^1]) is not { IsDateOrTime: true } type || (bucket && TypeOf(values[0]) is not { IsNumber: true }))
            return NotModelled(call, "a date that is not of a date or time type, or a width that is not a number, is not modelled.");
        if (bucket && call.Parameters.Count == 4)
        {
            var origin = Expression(call.Parameters[3], scope);
            if (origin.Shape is UnknownShape)
                return origin;
            if (TypeOf(origin) != type)
                return NotModelled(call, "an origin of another type than the date's is not modelled.");
        }

        // The width is converted to int, which a wider number may overflow (observed in the same test).
        var widthOverflows = bucket && TypeOf(values[0]) is { } width && !Narrower(width, call.Parameters[1], BuiltInType.Int, int.MinValue, int.MaxValue);
        return Typed(type, widthOverflows ? NullReasons.Join(Reasons(values), Overflow(call)) : Reasons(values), call);
    }

    /// <summary>
    /// Whether a number converts to <paramref name="to"/> without an overflow: it is an integer type narrower than it, or a
    /// literal within <paramref name="least"/> and <paramref name="most"/>.
    /// </summary>
    private static bool Narrower(ScalarType number, ScalarExpression expression, BuiltInType to, decimal least, decimal most) =>
        (number.IsInteger && number.Type.Digits is var (digits, _) && to.Digits is var (room, _) && digits <= room)
        || (NumberOf(expression) is { } n && n >= least && n <= most && n == decimal.Truncate(n));

    /// <summary>
    /// <c>SWITCHOFFSET(d, offset)</c> and <c>TODATETIMEOFFSET(d, offset)</c> of a date or time type, with an offset that is a
    /// string or a number of minutes: datetimeoffset of d's fractional digits, 7 for a date, 3 for a datetime and 0 for a
    /// smalldatetime; NULL when either is. An offset it can't read, or one past the range, is an error under any options, and a
    /// number wider than smallint may overflow
    /// (observed: Switchoffset_and_todatetimeoffset_are_datetimeoffset_of_the_dates_fractional_digits).
    /// </summary>
    private Value AtOffset(FunctionCall call, ReadScope scope)
    {
        var (values, unknown) = Arguments(call.Parameters, scope);
        if (unknown is not null)
            return unknown;
        if (TypeOf(values[0]) is not { IsDateOrTime: true } type || TypeOf(values[1]) is not ({ IsString: true } or { IsNumber: true }))
            return NotModelled(call, "a date that is not of a date or time type, or an offset that is neither a string nor a number, is not modelled.");
        var digits = type.Type == BuiltInType.Date ? 7 : type.Type == BuiltInType.DateTime ? 3 : type.Type == BuiltInType.SmallDateTime ? 0 : type.Precision;

        // A number of minutes is converted to smallint, which a wider number may overflow (observed in the same test).
        var overflows = TypeOf(values[1]) is { IsNumber: true } minutes && !Narrower(minutes, call.Parameters[1], BuiltInType.SmallInt, short.MinValue, short.MaxValue);
        return Typed(new ScalarType(BuiltInType.DateTimeOffset, Precision: digits), overflows ? NullReasons.Join(Reasons(values), Overflow(call)) : Reasons(values), call);
    }

    /// <summary>
    /// <c>d AT TIME ZONE zone</c> of a <c>datetime</c>, <c>smalldatetime</c>, <c>datetime2</c> or <c>datetimeoffset</c> and a
    /// string: datetimeoffset of d's fractional digits, 3 for a datetime and 0 for a smalldatetime; NULL only when either is. A
    /// zone the server doesn't know is an error, and a date moved past either end of the range is held at it, under any options,
    /// so nothing overflows. A date, a time, a string or a number is refused when the procedure is created, and so is a zone that
    /// isn't a string (observed: At_time_zone_is_datetimeoffset_of_the_dates_fractional_digits_and_null_only_for_a_null_date_or_zone).
    /// </summary>
    private Value AtTimeZone(AtTimeZoneCall call, ReadScope scope)
    {
        var (values, unknown) = Arguments([call.DateValue, call.TimeZone], scope);
        if (unknown is not null)
            return unknown;
        BuiltInType[] dates = [BuiltInType.DateTime, BuiltInType.SmallDateTime, BuiltInType.DateTime2, BuiltInType.DateTimeOffset];
        if (TypeOf(values[0]) is not { } type || !dates.Contains(type.Type) || StringOf(values[1]) is null)
        {
            return NotModelled(call, "AT TIME ZONE of a value that is not a datetime, smalldatetime, datetime2 or datetimeoffset, or in a zone that is not a string, is refused.");
        }

        var digits = type.Type == BuiltInType.DateTime ? 3 : type.Type == BuiltInType.SmallDateTime ? 0 : type.Precision;
        return Typed(new ScalarType(BuiltInType.DateTimeOffset, Precision: digits), Reasons(values), call);
    }

    /// <summary>
    /// <c>ISDATE(x)</c> of a string, a number, a datetime or a smalldatetime: int, 0 or 1, never null; NULL gives 0 (observed:
    /// Isdate_is_an_int_never_null, Isdate_and_isjson_give_0_or_1_and_isjson_null_for_null). Which strings read as dates depends on the session's language and DATEFORMAT, but the
    /// type and the nullability don't.
    /// </summary>
    private Value IsDate(FunctionCall call, ReadScope scope)
    {
        var value = Expression(call.Parameters[0], scope);
        if (value.Shape is UnknownShape)
            return value;
        var read = Strip(call.Parameters[0]) is NullLiteral
            || (TypeOf(value) is { } type && (type.IsString || type.IsNumber || type.IsBit || type.Type == BuiltInType.DateTime || type.Type == BuiltInType.SmallDateTime));
        if (!read)
            return NotModelled(call, "ISDATE of a value of this type is not modelled.");

        return Typed(ScalarType.Of(BuiltInType.Int), [], call);
    }
}
