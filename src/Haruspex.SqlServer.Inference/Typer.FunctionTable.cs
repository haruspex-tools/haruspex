using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;
using Haruspex.Ir;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using static Haruspex.Ir.IrNullRule;
using F = Haruspex.SqlServer.Semantics.BuiltInFunctions;

namespace Haruspex.SqlServer.Inference;

// The function table: a row for each built-in function of the catalogue (BuiltInFunctions), saying how inference types a call
// of it, and the one path a call is typed by (Function, below). A function the catalogue lacks is not modelled. The typing methods are
// the typer's own methods, which is why the table is a part of it; README.md in this folder ("Built-in functions", "Window
// functions") and the guide's chapter docs/internals/08-the-sql-server-type-system-as-data.md explain the rules.
internal sealed partial class Typer
{
    private const string DateKinds = "Date_functions_are_typed_by_their_kind_and_dateadd_by_its_dates_type";

    private const string DateIntegers = "A_date_function_given_an_integer_converts_it_and_any_date_beside_it_to_datetime_which_may_overflow";

    private const string DateNulls = "A_date_function_is_null_when_an_argument_is_but_eomonth_ignores_a_null_offset";

    private const string Rounded = "Datetrunc_and_date_bucket_keep_the_dates_type_and_are_null_only_for_a_null_date_or_width";

    private const string Offsets = "Switchoffset_and_todatetimeoffset_are_datetimeoffset_of_the_dates_fractional_digits";

    private const string Now = "The_current_date_and_time_functions_are_never_null";

    private const string Trims = "Trimming_and_case_keep_the_strings_length_and_make_a_fixed_length_string_varying";

    private const string Parts = "Left_right_and_substring_are_as_long_as_a_literal_length_allows";

    private const string Lengths = "Len_and_datalength_are_int_or_bigint_over_max_and_len_ignores_trailing_spaces";

    private const string Escapes = "String_escape_is_nvarchar_max_and_formatmessage_nvarchar_4000_writing_a_null_argument_as_null_in_brackets";

    private const string Codes = "Ascii_and_unicode_are_int_and_null_for_an_empty_string";

    private const string Positions = "Charindex_and_patindex_are_int_or_bigint_over_max_and_null_only_for_a_null_argument";

    private const string WideStarts = "A_start_or_length_past_int_overflows_in_charindex_substring_left_and_right_and_one_not_a_number_is_refused";

    private const string Counts = "Replicate_and_space_are_as_long_as_a_literal_count_allows_up_to_8000_bytes_and_null_for_a_negative_one";

    private const string Fixed = "Translate_is_8000_bytes_unless_its_string_is_max_and_soundex_and_difference_are_fixed";

    private const string Rounding = "Round_floor_ceiling_abs_and_sign_are_typed_by_their_argument";

    private const string RoundingOverflow = "Floor_ceiling_abs_and_sign_overflow_only_where_the_type_cannot_hold_the_result";

    private const string Floats = "Power_is_typed_by_its_first_argument_and_the_others_are_float";

    private const string LostPrecision = "Each_function_that_converts_a_decimal_or_averages_one_is_null_when_it_rounds_under_both_others_off";

    private const string LostPrecisionUnderTry = "Under_try_cast_a_loss_of_precision_is_null_whenever_numeric_roundabort_is_on";

    private const string Domains = "A_math_function_outside_its_domain_is_an_error_by_default_and_null_under_the_callers_options";

    private const string Trigonometry = "The_trigonometric_functions_and_pi_are_float_and_null_only_outside_their_domain_under_the_callers_options";

    private const string Angles = "Degrees_and_radians_keep_an_integers_type_or_a_floats_and_degrees_may_overflow";
    private const string DecimalAngles = "Degrees_and_radians_of_a_decimal_widen_it_to_38_digits_and_of_money_give_money_and_either_may_overflow";

    private const string Extremes = "Greatest_and_least_skip_nulls_and_cap_a_max_string_at_8000_bytes_which_a_longer_value_fails";

    private const string Tests = "Isjson_is_an_int_null_for_null_and_newid_a_uniqueidentifier_never_null";

    private const string Digested = "Hashbytes_is_varbinary_8000_of_its_algorithms_length_and_null_for_an_algorithm_it_does_not_run";

    private const string Compressed = "Compress_and_decompress_are_varbinary_max_null_only_for_null_and_decompress_of_other_bytes_is_an_error";

    private const string ZeroOrOne = "Isdate_and_isjson_give_0_or_1_and_isjson_null_for_null";

    private const string Expressions = "The_regular_expression_functions_but_regexp_like_are_there_at_every_compatibility_level_and_refuse_what_they_cannot_read";

    private const string Matches = "Regexp_instr_and_regexp_count_are_int_from_0_and_null_only_for_a_null_argument";

    private const string Choices = "Case_iif_and_choose_take_the_type_of_highest_precedence_over_their_results";

    private const string Coalescing = "Coalesce_takes_the_type_of_highest_precedence_and_isnull_the_first_arguments";

    private const string Windowed = "Window_functions_are_typed_by_their_kind_or_their_argument";

    private const string Ranking = "Ranking_is_never_null_and_percentiles_are_null_only_over_all_null_input";

    private const string Frames = "A_frame_that_may_leave_out_the_current_row_can_be_empty_and_give_null_over_a_never_null_column";

    private const string Statistics = "The_statistical_aggregates_are_float_and_the_sample_forms_null_over_fewer_than_two_values";

    private const string OneRow = "An_aggregate_without_group_by_returns_exactly_one_row_even_over_no_rows";

    private const string Refusals = "Json_value_eomonth_string_agg_and_percentiles_refuse_arguments_they_cannot_read";

    private const string OtherAggregates = "Checksum_agg_grouping_id_and_approx_count_distinct_are_aggregates_that_make_one_row";

    private static FunctionTyping Typing(
        BuiltInFunction function, (int, int?)? arity, string rule, IrNullRule[] nulls, string[] observedIn,
        Func<Typer, ScalarExpression, ReadScope, Value>? type, bool readsOnlyItsArguments = true, OtherForms otherForms = OtherForms.FormNotModelled) =>
        new(function, arity, otherForms, rule, [.. nulls], [.. observedIn], readsOnlyItsArguments, type);

    private static FunctionCall Call(ScalarExpression call) => (FunctionCall)call;

    private static FunctionTyping WindowRow(BuiltInFunction function, (int, int?) arity, string rule, IrNullRule[] nulls, string[] observedIn, WindowRule window) =>
        Typing(function, arity, rule, nulls, observedIn, (a, e, s) => a.Window(Call(e), s, function, window), readsOnlyItsArguments: false, OtherForms.WindowNotModelled);

    private static FunctionTyping AggregateRow(BuiltInFunction function, (int, int?)? arity, string rule, IrNullRule[] nulls, string[] observedIn, Func<Typer, ScalarExpression, ReadScope, Value> type) =>
        Typing(function, arity, rule, nulls, observedIn, type, readsOnlyItsArguments: true, OtherForms.AggregateNotModelled);

    /// <summary>Every row, one for each function of the catalogue.</summary>
    internal static ImmutableArray<FunctionTyping> FunctionTable { get; } =
    [
        // ---- dates ----
        Typing(ScalarFunctions.DateDiff, (3, 3), "int", [ArithmeticOverflow], [DateIntegers, DateKinds, DateNulls], (a, e, s) => a.DateDiff(Call(e), ScalarType.Of(BuiltInType.Int), s)),
        Typing(ScalarFunctions.DateDiffBig, (3, 3), "bigint", [ArithmeticOverflow], [DateIntegers, DateKinds, DateNulls], (a, e, s) => a.DateDiff(Call(e), ScalarType.Of(BuiltInType.BigInt), s)),
        Typing(ScalarFunctions.DateAdd, (3, 3), "the date's type, datetime for a string or an integer", [ArithmeticOverflow], [DateIntegers, DateKinds, DateNulls], (a, e, s) => a.DateAdd(Call(e), s)),
        Typing(ScalarFunctions.DatePart, (2, 2), "int", [ArithmeticOverflow], [DateIntegers, DateKinds, DateNulls], (a, e, s) => a.DatePart(Call(e), Call(e).Parameters[1], ScalarType.Of(BuiltInType.Int), s)),
        Typing(ScalarFunctions.Year, (1, 1), "int", [ArithmeticOverflow], [DateIntegers, DateKinds, DateNulls], (a, e, s) => a.DatePart(Call(e), Call(e).Parameters[0], ScalarType.Of(BuiltInType.Int), s)),
        Typing(ScalarFunctions.Month, (1, 1), "int", [ArithmeticOverflow], [DateIntegers, DateKinds], (a, e, s) => a.DatePart(Call(e), Call(e).Parameters[0], ScalarType.Of(BuiltInType.Int), s)),
        Typing(ScalarFunctions.Day, (1, 1), "int", [ArithmeticOverflow], [DateIntegers, DateKinds], (a, e, s) => a.DatePart(Call(e), Call(e).Parameters[0], ScalarType.Of(BuiltInType.Int), s)),
        Typing(ScalarFunctions.DateName, (2, 2), "nvarchar(30)", [ArithmeticOverflow], [DateIntegers, DateKinds, DateNulls], (a, e, s) => a.DatePart(Call(e), Call(e).Parameters[1], new ScalarType(BuiltInType.NVarChar, 30), s)),
        Typing(ScalarFunctions.EndOfMonth, (1, 2), "date", [], [DateKinds, DateNulls, Refusals], (a, e, s) => a.EndOfMonth(Call(e), s)),
        Typing(ScalarFunctions.DateFromParts, (3, 3), "date", [], [DateKinds, DateNulls], (a, e, s) => a.FromParts(Call(e), ScalarType.Of(BuiltInType.Date), s)),
        Typing(ScalarFunctions.DateTimeFromParts, (7, 7), "datetime", [], [DateKinds, DateNulls], (a, e, s) => a.FromParts(Call(e), ScalarType.Of(BuiltInType.DateTime), s)),
        Typing(ScalarFunctions.SmallDateTimeFromParts, (5, 5), "smalldatetime", [], [DateKinds], (a, e, s) => a.FromParts(Call(e), ScalarType.Of(BuiltInType.SmallDateTime), s)),
        Typing(ScalarFunctions.DateTime2FromParts, (8, 8), "datetime2 of the precision given", [], [DateKinds, DateNulls], (a, e, s) => a.FromParts(Call(e), ScalarType.Of(BuiltInType.DateTime2), s, precision: true)),
        Typing(ScalarFunctions.TimeFromParts, (5, 5), "time of the precision given", [], [DateKinds, DateNulls], (a, e, s) => a.FromParts(Call(e), ScalarType.Of(BuiltInType.Time), s, precision: true)),
        Typing(ScalarFunctions.DateTimeOffsetFromParts, (10, 10), "datetimeoffset of the precision given", [], [DateKinds], (a, e, s) => a.FromParts(Call(e), ScalarType.Of(BuiltInType.DateTimeOffset), s, precision: true)),
        Typing(ScalarFunctions.DateTruncate, (2, 2), "the date's type", [], [Rounded], (a, e, s) => a.DateRounded(Call(e), s, bucket: false)),
        Typing(ScalarFunctions.DateBucket, (3, 4), "the date's type", [ArithmeticOverflow], [Rounded], (a, e, s) => a.DateRounded(Call(e), s, bucket: true)),
        Typing(ScalarFunctions.SwitchOffset, (2, 2), "datetimeoffset of the date's fractional digits", [ArithmeticOverflow], [Offsets], (a, e, s) => a.AtOffset(Call(e), s)),
        Typing(ScalarFunctions.ToDateTimeOffset, (2, 2), "datetimeoffset of the date's fractional digits", [ArithmeticOverflow], [Offsets], (a, e, s) => a.AtOffset(Call(e), s)),
        Typing(ScalarFunctions.AtTimeZone, null, "datetimeoffset of the date's fractional digits", [], ["At_time_zone_is_datetimeoffset_of_the_dates_fractional_digits_and_null_only_for_a_null_date_or_zone"], (a, e, s) => e is AtTimeZoneCall call ? a.AtTimeZone(call, s) : a.NotModelled(e, "no function of this name can be called.")),
        Typing(ScalarFunctions.IsDate, (1, 1), "int, 0 or 1, never null", [], ["Isdate_is_an_int_never_null", ZeroOrOne], (a, e, s) => Within(a.IsDate(Call(e), s), 0, 1, "ISDATE")),
        Typing(ScalarFunctions.GetDate, (0, 0), "datetime, never null", [], [Now], (a, e, s) => a.Typed(ScalarType.Of(BuiltInType.DateTime), [], e), readsOnlyItsArguments: false),
        Typing(ScalarFunctions.GetUtcDate, (0, 0), "datetime, never null", [], [Now], (a, e, s) => a.Typed(ScalarType.Of(BuiltInType.DateTime), [], e), readsOnlyItsArguments: false),
        Typing(ScalarFunctions.SysDateTime, (0, 0), "datetime2(7), never null", [], [Now], (a, e, s) => a.Typed(new ScalarType(BuiltInType.DateTime2, Precision: 7), [], e), readsOnlyItsArguments: false),
        Typing(ScalarFunctions.SysUtcDateTime, (0, 0), "datetime2(7), never null", [], [Now], (a, e, s) => a.Typed(new ScalarType(BuiltInType.DateTime2, Precision: 7), [], e), readsOnlyItsArguments: false),
        Typing(ScalarFunctions.SysDateTimeOffset, (0, 0), "datetimeoffset(7), never null", [], [Now], (a, e, s) => a.Typed(new ScalarType(BuiltInType.DateTimeOffset, Precision: 7), [], e), readsOnlyItsArguments: false),
        Typing(ScalarFunctions.CurrentTimestamp, null, "datetime, never null", [], [Now], (a, e, s) => a.Typed(ScalarType.Of(BuiltInType.DateTime), [], e), readsOnlyItsArguments: false),

        // ---- strings ----
        Typing(ScalarFunctions.LTrim, (1, 2), "the string's length, varying", [], [Trims], (a, e, s) => a.Trim(Call(e), Call(e).Parameters[0], Call(e).Parameters.Count == 2 ? Call(e).Parameters[1] : null, s)),
        Typing(ScalarFunctions.RTrim, (1, 2), "the string's length, varying", [], [Trims], (a, e, s) => a.Trim(Call(e), Call(e).Parameters[0], Call(e).Parameters.Count == 2 ? Call(e).Parameters[1] : null, s)),

        // TRIM(chars FROM s) is given as (chars, s).
        Typing(ScalarFunctions.Trim, (1, 2), "the string's length, varying", [], [Trims], (a, e, s) => Call(e).Parameters.Count == 2 ? a.Trim(Call(e), Call(e).Parameters[1], Call(e).Parameters[0], s) : a.Trim(Call(e), Call(e).Parameters[0], null, s)),
        Typing(ScalarFunctions.Upper, (1, 1), "the string's length, varying", [], [Trims], (a, e, s) => a.Trim(Call(e), Call(e).Parameters[0], null, s)),
        Typing(ScalarFunctions.Lower, (1, 1), "the string's length, varying", [], [Trims], (a, e, s) => a.Trim(Call(e), Call(e).Parameters[0], null, s)),
        Typing(ScalarFunctions.Substring, (3, 3), "the string varying, as long as a literal length allows", [ArithmeticOverflow], [Parts, WideStarts], (a, e, s) => a.Part(e, Call(e).Parameters[0], [Call(e).Parameters[1], Call(e).Parameters[2]], Call(e).Parameters[2], s)),
        Typing(ScalarFunctions.Left, (2, 2), "the string varying, as long as a literal length allows", [ArithmeticOverflow], [Parts, WideStarts], (a, e, s) => a.Part(e, ((LeftFunctionCall)e).Parameters[0], [((LeftFunctionCall)e).Parameters[1]], ((LeftFunctionCall)e).Parameters[1], s), otherForms: OtherForms.ExpressionNotModelled),
        Typing(ScalarFunctions.Right, (2, 2), "the string varying, as long as a literal length allows", [ArithmeticOverflow], [Parts, WideStarts], (a, e, s) => a.Part(e, ((RightFunctionCall)e).Parameters[0], [((RightFunctionCall)e).Parameters[1]], ((RightFunctionCall)e).Parameters[1], s), otherForms: OtherForms.ExpressionNotModelled),
        Typing(ScalarFunctions.Len, (1, 1), "int, bigint over max", [], [Lengths], (a, e, s) => a.Length(Call(e), Call(e).Parameters[0], s, bytes: false)),
        Typing(ScalarFunctions.DataLength, (1, 1), "int, bigint over max", [], [Lengths], (a, e, s) => a.Length(Call(e), Call(e).Parameters[0], s, bytes: true)),
        Typing(ScalarFunctions.Replace, (3, 3), "varchar(8000) or nvarchar(4000), max when the string is", [], ["Replace_widens_to_8000_bytes_unless_its_string_is_max"], (a, e, s) => a.Replace(Call(e), s)),
        Typing(ScalarFunctions.StringEscape, (2, 2), "nvarchar(max)", [], [Escapes], (a, e, s) => a.StringEscape(Call(e), s)),
        Typing(ScalarFunctions.FormatMessage, (1, null), "nvarchar(4000)", [], [Escapes], (a, e, s) => a.FormatMessage(Call(e), s)),
        Typing(ScalarFunctions.Concat, null, "as long as its arguments together, never null", [], ["Concat_is_as_long_as_its_arguments_together_up_to_8000_bytes_and_never_null"], (a, e, s) => a.Concat(Call(e), s, separated: false)),
        Typing(ScalarFunctions.ConcatWithSeparator, null, "as long as its arguments and separators together, never null", [], ["Concat_is_as_long_as_its_arguments_together_up_to_8000_bytes_and_never_null"], (a, e, s) => a.Concat(Call(e), s, separated: true)),

        Typing(ScalarFunctions.Ascii, (1, 1), "int, 0 to 255", [OutOfRange], [Codes, "Ascii_is_a_byte_from_0_to_255_whatever_the_character_or_collation"], (a, e, s) => Within(a.FirstCode(Call(e), s), 0, 255, "ASCII")),
        Typing(ScalarFunctions.Unicode, (1, 1), "int", [OutOfRange], [Codes], (a, e, s) => a.FirstCode(Call(e), s)),
        Typing(ScalarFunctions.CharOf, (1, 1), "char(1)", [OutOfRange, ArithmeticOverflow], ["Char_is_char_1_and_null_outside_0_to_255_and_overflows_past_int"], (a, e, s) => a.CharOf(Call(e), s)),
        Typing(ScalarFunctions.CharIndex, (2, 3), "int, bigint over max", [ArithmeticOverflow], [Positions, WideStarts], (a, e, s) => a.Position(Call(e), s)),
        Typing(ScalarFunctions.PatIndex, (2, 2), "int, bigint over max", [], [Positions], (a, e, s) => a.Position(Call(e), s)),
        Typing(ScalarFunctions.Replicate, (2, 2), "the string varying, as long as a literal count allows up to 8000 bytes", [OutOfRange], [Counts], (a, e, s) => a.Replicate(Call(e), s)),
        Typing(ScalarFunctions.Space, (1, 1), "varchar of a literal count up to 8000, or 8000", [OutOfRange], [Counts], (a, e, s) => a.Space(Call(e), s)),
        Typing(ScalarFunctions.Reverse, (1, 1), "the string's length, varying", [], ["Reverse_keeps_the_strings_length_and_makes_it_varying"], (a, e, s) => a.Trim(Call(e), Call(e).Parameters[0], null, s)),
        Typing(ScalarFunctions.Stuff, (4, 4), "varying, as long as both strings less a literal length, or 8000 bytes", [OutOfRange], ["Stuff_is_as_long_as_its_strings_less_a_literal_length_and_null_for_a_start_or_length_it_cannot_use"], (a, e, s) => a.Stuff(Call(e), s)),
        Typing(ScalarFunctions.QuoteName, (1, 2), "nvarchar(258)", [OutOfRange], ["Quotename_is_nvarchar_258_and_null_past_128_characters_or_for_a_quote_it_cannot_use"], (a, e, s) => a.QuoteName(Call(e), s)),
        Typing(ScalarFunctions.Str, (1, 3), "varchar(10), or of a literal length, or 8000", [OutOfRange], ["Str_is_varchar_of_its_literal_length_10_by_default_and_null_for_a_length_it_cannot_use"], (a, e, s) => a.Str(Call(e), s)),
        Typing(ScalarFunctions.Translate, (3, 3), "varchar(8000) or nvarchar(4000), max when the string is", [], [Fixed], (a, e, s) => a.Translate(Call(e), s)),
        Typing(ScalarFunctions.Soundex, (1, 1), "varchar(5)", [], [Fixed], (a, e, s) => a.Phonetic(Call(e), new ScalarType(BuiltInType.VarChar, 5), s)),
        Typing(ScalarFunctions.Difference, (2, 2), "int", [], [Fixed], (a, e, s) => a.Phonetic(Call(e), ScalarType.Of(BuiltInType.Int), s)),

        // ---- binary values ----
        Typing(ScalarFunctions.HashBytes, (2, 2), "varbinary(8000), exactly as long as a literal algorithm's digest", [OutOfRange], [Digested], (a, e, s) => a.HashBytes(Call(e), s)),
        Typing(ScalarFunctions.Compress, (1, 1), "varbinary(max)", [], [Compressed], (a, e, s) => a.Compression(Call(e), s, decompress: false)),
        Typing(ScalarFunctions.Decompress, (1, 1), "varbinary(max)", [], [Compressed], (a, e, s) => a.Compression(Call(e), s, decompress: true)),

        // ---- numbers ----
        Typing(ScalarFunctions.Round, (2, 3), "the argument's type, widened", [ArithmeticOverflow], [Rounding, "Round_overflows_when_it_carries_into_a_digit_its_type_lacks_unless_it_truncates"], (a, e, s) => a.Numeric(Call(e), s, a.Round, onlyTheSecondCounts: true)),
        Typing(ScalarFunctions.Floor, (1, 1), "the argument's type, widened, a decimal of no places", [ArithmeticOverflow], [Rounding, RoundingOverflow], (a, e, s) => a.Numeric(Call(e), s, a.FloorOrCeiling)),
        Typing(ScalarFunctions.Ceiling, (1, 1), "the argument's type, widened, a decimal of no places", [ArithmeticOverflow], [Rounding, RoundingOverflow], (a, e, s) => a.Numeric(Call(e), s, a.FloorOrCeiling)),
        Typing(ScalarFunctions.Abs, (1, 1), "the argument's type, widened", [ArithmeticOverflow], [Rounding, RoundingOverflow], (a, e, s) => a.Numeric(Call(e), s, a.Abs)),
        Typing(ScalarFunctions.Sign, (1, 1), "the argument's type, widened", [], [Rounding, RoundingOverflow], (a, e, s) => a.Numeric(Call(e), s, a.Sign)),
        Typing(ScalarFunctions.Power, (2, 2), "the first argument's type, a decimal of 38 digits", [ArithmeticOverflow, DomainError], [Floats, "Power_computes_in_float_and_overflows_or_divides_by_zero_as_its_type_allows"], (a, e, s) => a.Numeric(Call(e), s, a.Power)),
        Typing(ScalarFunctions.Square, (1, 1), "float", [ArithmeticOverflow], [Floats, Domains], (a, e, s) => a.Numeric(Call(e), s, a.Square)),
        Typing(ScalarFunctions.Exp, (1, 1), "float", [ArithmeticOverflow], [Floats, Domains], (a, e, s) => a.Numeric(Call(e), s, a.Exp)),
        Typing(ScalarFunctions.Sqrt, (1, 1), "float", [DomainError], [Floats, Domains], (a, e, s) => a.Numeric(Call(e), s, a.Sqrt)),
        Typing(ScalarFunctions.Log, (1, 2), "float", [DomainError], [Floats, Domains], (a, e, s) => a.Numeric(Call(e), s, a.Log)),
        Typing(ScalarFunctions.Log10, (1, 1), "float", [DomainError], [Floats, Domains], (a, e, s) => a.Numeric(Call(e), s, a.Log)),

        Typing(ScalarFunctions.Sin, (1, 1), "float", [], [Trigonometry], (a, e, s) => a.Numeric(Call(e), s, n => a.Trigonometric(n, domain: false))),
        Typing(ScalarFunctions.Cos, (1, 1), "float", [], [Trigonometry], (a, e, s) => a.Numeric(Call(e), s, n => a.Trigonometric(n, domain: false))),
        Typing(ScalarFunctions.Tan, (1, 1), "float", [], [Trigonometry], (a, e, s) => a.Numeric(Call(e), s, n => a.Trigonometric(n, domain: false))),
        Typing(ScalarFunctions.Cot, (1, 1), "float", [DomainError, ArithmeticOverflow], [Trigonometry], (a, e, s) => a.Numeric(Call(e), s, n => a.Trigonometric(n, domain: true, overflow: true))),
        Typing(ScalarFunctions.Asin, (1, 1), "float", [DomainError], [Trigonometry], (a, e, s) => a.Numeric(Call(e), s, n => a.Trigonometric(n, domain: true, bitInDomain: true))),
        Typing(ScalarFunctions.Acos, (1, 1), "float", [DomainError], [Trigonometry], (a, e, s) => a.Numeric(Call(e), s, n => a.Trigonometric(n, domain: true, bitInDomain: true))),
        Typing(ScalarFunctions.Atan, (1, 1), "float", [], [Trigonometry], (a, e, s) => a.Numeric(Call(e), s, n => a.Trigonometric(n, domain: false))),
        Typing(ScalarFunctions.Atn2, (2, 2), "float", [DomainError], [Trigonometry], (a, e, s) => a.Numeric(Call(e), s, n => a.Trigonometric(n, domain: true))),
        Typing(ScalarFunctions.Degrees, (1, 1), "int, bigint or float, as the argument; decimal(38, s or 18) of a decimal; money of money", [ArithmeticOverflow], [Angles, DecimalAngles], (a, e, s) => a.Numeric(Call(e), s, n => a.Angle(n, degrees: true))),
        Typing(ScalarFunctions.Radians, (1, 1), "int, bigint or float, as the argument; decimal(38, s or 18) of a decimal; money of money", [ArithmeticOverflow], [Angles, DecimalAngles], (a, e, s) => a.Numeric(Call(e), s, n => a.Angle(n, degrees: false))),
        Typing(ScalarFunctions.Pi, (0, 0), "float, never null", [], [Trigonometry], (a, e, s) => a.Typed(ScalarType.Of(BuiltInType.Float), [], e)),

        // ---- conversions and choices ----
        Typing(ScalarFunctions.Cast, null, "the target type", [ArithmeticOverflow, IrNullRule.PrecisionLoss, IrNullRule.NullLiteral], ["Cast_and_convert_are_of_the_target_type_whatever_the_style", "The_conversion_chart_is_what_the_server_allows_implicitly_explicitly_or_not_at_all", LostPrecision], (a, e, s) => a.Cast(((CastCall)e).DataType, ((CastCall)e).Parameter, e, s)),
        Typing(ScalarFunctions.Convert, null, "the target type, whatever the style", [ArithmeticOverflow, IrNullRule.PrecisionLoss, IrNullRule.NullLiteral], ["Cast_and_convert_are_of_the_target_type_whatever_the_style", LostPrecision], (a, e, s) => a.Cast(((ConvertCall)e).DataType, ((ConvertCall)e).Parameter, e, s, styled: ((ConvertCall)e).Style is not null)),
        Typing(ScalarFunctions.TryCast, null, "the target type", [FailedConversion, IrNullRule.PrecisionLoss, IrNullRule.NullLiteral], ["Try_cast_and_try_convert_are_null_when_the_conversion_fails", LostPrecisionUnderTry], (a, e, s) => a.Cast(((TryCastCall)e).DataType, ((TryCastCall)e).Parameter, e, s, attempt: true)),
        Typing(ScalarFunctions.TryConvert, null, "the target type, whatever the style", [FailedConversion, IrNullRule.PrecisionLoss, IrNullRule.NullLiteral], ["Try_cast_and_try_convert_are_null_when_the_conversion_fails", LostPrecisionUnderTry], (a, e, s) => a.Cast(((TryConvertCall)e).DataType, ((TryConvertCall)e).Parameter, e, s, styled: ((TryConvertCall)e).Style is not null, attempt: true)),
        Typing(ScalarFunctions.IsNull, (2, 2), "the first argument's type", [ArithmeticOverflow, IrNullRule.PrecisionLoss], ["Isnull_converts_its_second_argument_to_the_firsts_type_cutting_a_string_short", Coalescing, LostPrecision], (a, e, s) => a.IsNull(Call(e), s), otherForms: OtherForms.ExpressionNotModelled),
        Typing(ScalarFunctions.Coalesce, null, "the type of highest precedence over its arguments", [ArithmeticOverflow, IrNullRule.ReadTwice, IrNullRule.NullLiteral], [Coalescing, "Coalesce_reads_a_subquery_twice_so_it_can_be_null_where_isnull_cannot", "Assigned_to_a_variable_coalesce_and_case_over_json_are_the_same_text_whether_or_not_the_server_folds_them"], (a, e, s) => a.Coalesce((CoalesceExpression)e, s)),
        Typing(ScalarFunctions.NullIf, null, "the first argument's type", [NullWhenEqual], ["Nullif_is_its_first_arguments_type_but_an_integer_literal_is_the_smallest_integer_holding_it"], (a, e, s) => a.NullIf((NullIfExpression)e, s)),
        Typing(ScalarFunctions.Iif, null, "the type of highest precedence over its results", [ArithmeticOverflow, IrNullRule.PrecisionLoss], [Choices, LostPrecision], (a, e, s) => a.Iif((IIfCall)e, s)),
        Typing(ScalarFunctions.Choose, (2, null), "the type of highest precedence over its results", [OutOfRange, ArithmeticOverflow, IrNullRule.PrecisionLoss], [Choices, "Case_without_else_choose_out_of_range_and_nullif_of_equal_values_are_null", LostPrecision], (a, e, s) => a.Choose(Call(e), s), otherForms: OtherForms.ExpressionNotModelled),

        Typing(ScalarFunctions.Greatest, (1, null), "the values' type, when of one kind", [], [Extremes], (a, e, s) => a.Extreme(Call(e), s)),
        Typing(ScalarFunctions.Least, (1, null), "the values' type, when of one kind", [], [Extremes], (a, e, s) => a.Extreme(Call(e), s)),

        // ---- JSON ----
        Typing(ScalarFunctions.JsonValue, (2, 2), "nvarchar(4000), or the text of what JSON of known shape holds at its path", [JsonNullOrAbsent], ["Json_value_is_nvarchar_4000_and_null_for_a_json_null_a_missing_path_an_object_or_a_long_string", "Json_value_over_json_built_here_gives_its_scalars_as_text", "Json_value_gives_null_for_what_it_cannot_return_under_lax_and_errs_under_strict", Refusals], (a, e, s) => a.RefusedJsonDocument(Call(e), s) ?? a.TracedJsonValue(Call(e), s) ?? a.JsonValue(Call(e))),
        Typing(ScalarFunctions.JsonQuery, (1, 2), "the JSON it is given, or the object or array at its path, as JSON", [JsonNullOrAbsent], ["Under_json_query_coalesce_case_and_isnull_with_a_json_literal_give_the_json_of_the_branch_taken", "Json_query_with_a_path_gives_the_object_or_array_there_and_null_under_lax_for_anything_else"], (a, e, s) => Call(e).Parameters.Count == 2 ? a.JsonQueryAt(Call(e), s) : a.JsonQuery(Call(e), s), readsOnlyItsArguments: false, otherForms: OtherForms.ExpressionNotModelled),
        Typing(ScalarFunctions.JsonObject, null, "an object of its keys, never null", [], ["Json_object_writes_a_repeated_literal_key_twice"], (a, e, s) => a.Constructor(Call(e), s, ScalarFunctions.JsonObject), readsOnlyItsArguments: false),
        Typing(ScalarFunctions.JsonArray, null, "an array or tuple of its values, never null", [], ["An_aggregate_over_no_rows_is_null_not_empty"], (a, e, s) => a.Constructor(Call(e), s, ScalarFunctions.JsonArray), readsOnlyItsArguments: false),

        Typing(ScalarFunctions.IsJson, (1, 2), "int, 0 or 1", [], [Tests, ZeroOrOne], (a, e, s) => Within(a.IsJson(Call(e), s), 0, 1, "ISJSON")),
        Typing(ScalarFunctions.NewId, (0, 0), "uniqueidentifier, never null", [], [Tests], (a, e, s) => a.Typed(ScalarType.Of(BuiltInType.UniqueIdentifier), [], e), readsOnlyItsArguments: false),

        // ---- names ----
        Typing(ScalarFunctions.ParseName, (2, 2), "nvarchar(128)", [OutOfRange, ArithmeticOverflow], ["Parsename_is_nvarchar_128_and_null_for_a_part_it_cannot_find_or_a_name_it_cannot_read"], (a, e, s) => a.ParseName(Call(e), s)),

        // Without an id it names the database the call runs in, which the arguments don't say: it reads more than them.
        Typing(ScalarFunctions.DbName, (0, 1), "nvarchar(128), never null without an id", [OutOfRange, ArithmeticOverflow], ["Db_name_is_nvarchar_128_never_null_for_the_current_database_and_null_for_an_id_the_caller_cannot_see"], (a, e, s) => a.DbName(Call(e), s), readsOnlyItsArguments: false),

        // ---- regular expressions ----
        Typing(ScalarFunctions.RegexpReplace, (2, 6), "varchar(8000) or nvarchar(4000) as the string is, max when it is", [ArithmeticOverflow], [Expressions, "Regexp_replace_is_8000_bytes_or_max_of_its_strings_kind_and_cuts_a_longer_result_short"], (a, e, s) => a.RegexpReplace(Call(e), s)),
        Typing(ScalarFunctions.RegexpSubstr, (2, 6), "the string's length, varying", [OutOfRange, ArithmeticOverflow], [Expressions, "Regexp_substr_is_its_strings_length_varying_and_null_when_nothing_matches"], (a, e, s) => a.RegexpSubstr(Call(e), s)),
        Typing(ScalarFunctions.RegexpInstr, (2, 7), "int, from 0", [ArithmeticOverflow], [Expressions, Matches], (a, e, s) => Within(a.RegexpPosition(Call(e), s, ScalarFunctions.RegexpInstr), 0, int.MaxValue, "REGEXP_INSTR")),
        Typing(ScalarFunctions.RegexpCount, (2, 4), "int, from 0", [ArithmeticOverflow], [Expressions, Matches], (a, e, s) => Within(a.RegexpPosition(Call(e), s, ScalarFunctions.RegexpCount), 0, int.MaxValue, "REGEXP_COUNT")),

        // ---- aggregates ----
        AggregateRow(AggregateFunctions.Count, (1, 1), "int", [CountOverflow], ["Count_over_more_rows_than_int_holds_is_null_under_the_callers_ansi_warnings_and_arithabort_off", OneRow], (a, e, s) => a.Count(Call(e), s, big: false)),
        AggregateRow(AggregateFunctions.CountBig, (1, 1), "bigint, never null", [], ["Count_over_more_rows_than_int_holds_is_null_under_the_callers_ansi_warnings_and_arithabort_off"], (a, e, s) => a.Count(Call(e), s, big: true)),
        AggregateRow(AggregateFunctions.Sum, (1, 1), "the argument's type, widened", [EmptyAggregate, ArithmeticOverflow], ["Sum_widens_its_argument"], (a, e, s) => a.Sum(Call(e), s)),
        AggregateRow(AggregateFunctions.Avg, (1, 1), "the argument's type, widened", [EmptyAggregate, ArithmeticOverflow, IrNullRule.PrecisionLoss], ["Avg_is_typed_by_its_argument_with_or_without_a_window", "Avg_is_null_over_no_rows_and_overflows_as_sum_does", LostPrecision], (a, e, s) => a.Average(Call(e), s, AggregateFunctions.Avg)),
        AggregateRow(AggregateFunctions.Min, (1, 1), "the argument, with its facets", [EmptyAggregate], [OneRow, "Min_and_max_of_a_bit_are_refused_with_or_without_a_window"], (a, e, s) => a.MinOrMax(Call(e), s, AggregateFunctions.Min)),
        AggregateRow(AggregateFunctions.Max, (1, 1), "the argument, with its facets", [EmptyAggregate], [OneRow, "Min_and_max_of_a_bit_are_refused_with_or_without_a_window"], (a, e, s) => a.MinOrMax(Call(e), s, AggregateFunctions.Max)),
        AggregateRow(AggregateFunctions.Stdev, (1, 1), "float", [EmptyAggregate, TooFewValues, ArithmeticOverflow], [Statistics, "The_statistical_aggregates_overflow_only_over_float"], (a, e, s) => a.Average(Call(e), s, AggregateFunctions.Stdev)),
        AggregateRow(AggregateFunctions.StdevP, (1, 1), "float", [EmptyAggregate, ArithmeticOverflow], [Statistics], (a, e, s) => a.Average(Call(e), s, AggregateFunctions.StdevP)),
        AggregateRow(AggregateFunctions.Var, (1, 1), "float", [EmptyAggregate, TooFewValues, ArithmeticOverflow], [Statistics], (a, e, s) => a.Average(Call(e), s, AggregateFunctions.Var)),
        AggregateRow(AggregateFunctions.VarP, (1, 1), "float", [EmptyAggregate, ArithmeticOverflow], [Statistics], (a, e, s) => a.Average(Call(e), s, AggregateFunctions.VarP)),
        AggregateRow(AggregateFunctions.StringAgg, (2, 2), "varchar(8000) or nvarchar(4000), max over max", [EmptyAggregate], ["String_agg_is_8000_bytes_unless_max_and_null_over_no_rows_or_only_nulls", "String_agg_past_its_limit_is_an_error_even_under_the_callers_ansi_warnings_and_arithabort_off", Refusals], (a, e, s) => a.StringAggregate(Call(e), s)),
        AggregateRow(AggregateFunctions.ChecksumAgg, null, "not modelled: an aggregate, so one row without GROUP BY", [], [OtherAggregates], (a, e, s) => a.AggregateNotModelled(Call(e))),
        AggregateRow(AggregateFunctions.Grouping, null, "not modelled: an aggregate", [], ["Rollup_cube_and_grouping_sets_add_rows_whose_grouped_columns_are_null"], (a, e, s) => a.AggregateNotModelled(Call(e))),
        AggregateRow(AggregateFunctions.GroupingId, null, "not modelled: an aggregate", [], [OtherAggregates], (a, e, s) => a.AggregateNotModelled(Call(e))),
        AggregateRow(AggregateFunctions.ApproxCountDistinct, null, "not modelled: an aggregate, so one row without GROUP BY", [], [OtherAggregates], (a, e, s) => a.AggregateNotModelled(Call(e))),
        AggregateRow(AggregateFunctions.JsonArrayAgg, null, "an array of its values, null over no rows", [EmptyAggregate], ["Json_arrayagg_follows_its_order_by"], (a, e, s) => a.Constructor(Call(e), s, AggregateFunctions.JsonArrayAgg)),
        AggregateRow(AggregateFunctions.JsonObjectAgg, null, "an object of its keys, null over no rows", [EmptyAggregate], ["Json_objectagg_keeps_every_duplicate_key"], (a, e, s) => a.Constructor(Call(e), s, AggregateFunctions.JsonObjectAgg)),

        // ---- window functions ----
        WindowRow(WindowFunctions.RowNumber, (0, 0), "bigint, never null", [], [Windowed, Ranking], new WindowRule(WindowResult.Fixed, BuiltInType.BigInt)),
        WindowRow(WindowFunctions.Rank, (0, 0), "bigint, never null", [], [Windowed, Ranking], new WindowRule(WindowResult.Fixed, BuiltInType.BigInt)),
        WindowRow(WindowFunctions.DenseRank, (0, 0), "bigint, never null", [], [Windowed, Ranking], new WindowRule(WindowResult.Fixed, BuiltInType.BigInt)),
        WindowRow(WindowFunctions.NTile, (1, 1), "bigint, never null", [], [Windowed, Ranking], new WindowRule(WindowResult.Fixed, BuiltInType.BigInt)),
        WindowRow(WindowFunctions.Count, (1, 1), "int", [CountOverflow], [Windowed], new WindowRule(WindowResult.Fixed, BuiltInType.Int, CountOverflow: true)),
        WindowRow(WindowFunctions.CountBig, (1, 1), "bigint, never null", [], [Windowed], new WindowRule(WindowResult.Fixed, BuiltInType.BigInt)),
        WindowRow(WindowFunctions.Sum, (1, 1), "as the aggregate", [ArithmeticOverflow, EmptyFrame], [Windowed, Frames], new WindowRule(WindowResult.Sum, EmptyFrame: true)),
        WindowRow(WindowFunctions.Avg, (1, 1), "as the aggregate", [ArithmeticOverflow, IrNullRule.PrecisionLoss, EmptyFrame], ["Avg_is_typed_by_its_argument_with_or_without_a_window", LostPrecision], new WindowRule(WindowResult.Statistic, EmptyFrame: true, Average: true)),
        WindowRow(WindowFunctions.Stdev, (1, 1), "float", [TooFewValues, ArithmeticOverflow, EmptyFrame], [Statistics], new WindowRule(WindowResult.Statistic, EmptyFrame: true, TooFewValues: true)),
        WindowRow(WindowFunctions.StdevP, (1, 1), "float", [ArithmeticOverflow, EmptyFrame], [Statistics], new WindowRule(WindowResult.Statistic, EmptyFrame: true)),
        WindowRow(WindowFunctions.Var, (1, 1), "float", [TooFewValues, ArithmeticOverflow, EmptyFrame], [Statistics], new WindowRule(WindowResult.Statistic, EmptyFrame: true, TooFewValues: true)),
        WindowRow(WindowFunctions.VarP, (1, 1), "float", [ArithmeticOverflow, EmptyFrame], [Statistics], new WindowRule(WindowResult.Statistic, EmptyFrame: true)),
        WindowRow(WindowFunctions.Min, (1, 1), "the argument, with its facets", [EmptyFrame], [Windowed, "Min_and_max_of_a_bit_are_refused_with_or_without_a_window"], new WindowRule(WindowResult.Argument, EmptyFrame: true, RefusesBit: true)),
        WindowRow(WindowFunctions.Max, (1, 1), "the argument, with its facets", [EmptyFrame], [Windowed, "Min_and_max_of_a_bit_are_refused_with_or_without_a_window"], new WindowRule(WindowResult.Argument, EmptyFrame: true, RefusesBit: true)),
        WindowRow(WindowFunctions.FirstValue, (1, 1), "the argument, with its facets", [EmptyFrame], [Windowed, Frames], new WindowRule(WindowResult.Argument, EmptyFrame: true)),
        WindowRow(WindowFunctions.LastValue, (1, 1), "the argument, with its facets", [EmptyFrame], [Windowed, Frames], new WindowRule(WindowResult.Argument, EmptyFrame: true)),
        WindowRow(WindowFunctions.Lag, (1, 3), "the argument's type, its facets without a default", [WindowEdge, ArithmeticOverflow, IrNullRule.PrecisionLoss], ["Lag_and_lead_are_null_past_the_partitions_edge_unless_a_default_is_given_which_is_converted_to_the_columns_type", LostPrecision], new WindowRule(WindowResult.Offset)),
        WindowRow(WindowFunctions.Lead, (1, 3), "the argument's type, its facets without a default", [WindowEdge, ArithmeticOverflow, IrNullRule.PrecisionLoss], [Windowed], new WindowRule(WindowResult.Offset)),
        WindowRow(WindowFunctions.PercentileCont, (1, 1), "float", [], [Ranking, Refusals], new WindowRule(WindowResult.PercentileBetweenValues)),
        WindowRow(WindowFunctions.PercentileDisc, (1, 1), "the argument, with its facets", [], [Ranking, Refusals], new WindowRule(WindowResult.PercentileOfValues)),

        // ---- rowsets, typed by the relation readers ----
        Typing(RowsetFunctions.GenerateSeries, (2, 3), "one column, value, of its arguments' type, never null", [], ["Generate_series_gives_value_of_its_arguments_type_never_null_and_no_rows_for_a_null_argument"], null, readsOnlyItsArguments: false),
        Typing(RowsetFunctions.StringSplit, (2, 3), "value, of the string's type varying, never null; ordinal, a bigint from 1, with the literal 1", [], ["String_split_gives_value_of_its_strings_type_varying_never_null_and_ordinal_only_when_asked", "String_split_needs_compatibility_level_130_and_has_its_ordinal_from_there"], null, readsOnlyItsArguments: false),
        Typing(RowsetFunctions.OpenJson, null, "its WITH columns as declared, or key, value and type", [], ["Openjson_with_over_json_built_here_converts_its_own_types_exactly_and_cuts_strings_short", "Openjson_without_with_over_json_built_here_gives_its_keys_and_their_types"], null, readsOnlyItsArguments: false),
    ];

    private static readonly FrozenDictionary<BuiltInFunction, FunctionTyping> TypingByFunction = FunctionTable.ToFrozenDictionary(t => t.Function);

    /// <summary>
    /// The row of the built-in function an expression calls: a call without a schema by its name and form (with OVER a window
    /// function, else an aggregate or a scalar function), or the syntax ScriptDom gives a function of its own. Null for anything
    /// else, and for a function the catalogue lacks.
    /// </summary>
    private static FunctionTyping? TypingOf(ScalarExpression expression)
    {
        var function = expression switch
        {
            FunctionCall { CallTarget: null, OverClause: not null } call => F.Named(call.FunctionName.Value, FunctionForm.Window),
            FunctionCall { CallTarget: null } call =>
                F.Named(call.FunctionName.Value, FunctionForm.Aggregate) ?? F.Named(call.FunctionName.Value, FunctionForm.Scalar),
            LeftFunctionCall => ScalarFunctions.Left,
            RightFunctionCall => ScalarFunctions.Right,
            ParameterlessCall { ParameterlessCallType: ParameterlessCallType.CurrentTimestamp } => ScalarFunctions.CurrentTimestamp,
            AtTimeZoneCall => ScalarFunctions.AtTimeZone,
            CastCall => ScalarFunctions.Cast,
            ConvertCall => ScalarFunctions.Convert,
            TryCastCall => ScalarFunctions.TryCast,
            TryConvertCall => ScalarFunctions.TryConvert,
            CoalesceExpression => ScalarFunctions.Coalesce,
            NullIfExpression => ScalarFunctions.NullIf,
            IIfCall => ScalarFunctions.Iif,
            _ => null,
        };
        return function is null ? null : TypingByFunction[function];
    }

    /// <summary>
    /// A call of a built-in function, typed by its row (<see cref="TypingOf"/>): a number of arguments the row doesn't read gives
    /// what the row says of other forms. Null when the expression calls no function the table holds; a call with OVER of one it
    /// lacks is a window function not modelled.
    /// </summary>
    private Value? Function(ScalarExpression expression, ReadScope scope)
    {
        if (TypingOf(expression) is not { Type: { } type } typing)
        {
            return expression is FunctionCall { CallTarget: null, OverClause: not null } window ? WindowNotModelled(window) : null;
        }

        int? count = expression switch
        {
            FunctionCall call => call.Parameters.Count,
            LeftFunctionCall left => left.Parameters.Count,
            RightFunctionCall right => right.Parameters.Count,
            _ => null,
        };
        if (typing.Arity is var (least, most) && count is { } n && (n < least || n > most))
        {
            return typing.OtherForms switch
            {
                OtherForms.FormNotModelled => NotModelled(expression, "this form of the function is not modelled."),
                OtherForms.AggregateNotModelled => AggregateNotModelled((FunctionCall)expression),
                OtherForms.WindowNotModelled => WindowNotModelled((FunctionCall)expression),
                _ => Value.Unknown(Unsupported(IrUnknownReason.UnsupportedExpression, $"The expression {Text(expression)} is not modelled yet.", At(expression))),
            };
        }

        return type(this, expression, scope);
    }

    /// <summary>
    /// An integer a function gives only within a range the engine fixes: its least and greatest values are those, in place of its
    /// type's own, from the engine (<see cref="IrFactSourceKind.Engine"/>, named by the function). The function table applies it,
    /// beside the rule it states: ASCII's byte, 0 to 255, whatever the character or collation, since it reads the string as
    /// varchar (observed: Ascii_is_a_byte_from_0_to_255_whatever_the_character_or_collation); ISDATE's and ISJSON's 0 or 1
    /// (Isdate_and_isjson_give_0_or_1_and_isjson_null_for_null); REGEXP_INSTR's and REGEXP_COUNT's from 0; and STRING_SPLIT's
    /// ordinal, a bigint from 1.
    /// </summary>
    private static Value Within(Value value, long least, long greatest, string function)
    {
        if (value.Shape is not ScalarShape scalar)
            return value;
        var from = new IrFactSource(IrFactSourceKind.Engine, function);
        var facets = (scalar.Type.Facets ?? new IrFacets()) with
        {
            Min = new IrBound(least.ToString(CultureInfo.InvariantCulture), true, from),
            Max = new IrBound(greatest.ToString(CultureInfo.InvariantCulture), true, from),
        };
        return value with { Shape = scalar with { Type = scalar.Type with { Facets = facets } } };
    }
}
