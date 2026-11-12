using Haruspex.Ir;
using Haruspex.SqlServer.Inference.Lattice;
using Haruspex.SqlServer.Inference.Typing;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

// The date and string functions, JSON_VALUE, STRING_AGG and GENERATE_SERIES. Types come from ScalarType.cs; README.md
// in this folder ("Built-in functions") gives the rules and the observations they rest on (FunctionObservationTests in the
// differential tests). Window functions are in ProcedureAnalyzer.Windows.cs.
internal sealed partial class ProcedureAnalyzer
{
    /// <summary>
    /// <c>JSON_VALUE</c> of a document the server refuses when the procedure is created: a value that isn't text (8116), or a
    /// string literal that isn't a JSON object or array (13609); null for any other (observed:
    /// Json_value_eomonth_string_agg_and_percentiles_refuse_arguments_they_cannot_read).
    /// </summary>
    private Value? RefusedJsonDocument(FunctionCall call, BindingScope scope)
    {
        var document = call.Parameters[0];
        if (Strip(document) is StringLiteral literal)
            return JsonLiterals.IsDocumentText(literal.Value) ? null : NotModelled(call, "SQL Server refuses it: its document is not a JSON object or array.");
        var value = Expression(document, scope);
        return !value.IsLiveJson && value.Shape is ScalarShape && TypeOf(value) is { IsString: false } type
            ? NotModelled(call, $"SQL Server refuses JSON_VALUE of {type.Text}.")
            : null;
    }

    /// <summary>Reads arguments, giving the first that is unknown, if any.</summary>
    private (List<Value> Values, Value? Unknown) Arguments(IEnumerable<ScalarExpression> arguments, BindingScope scope)
    {
        var values = arguments.Select(a => Expression(a, scope)).ToList();
        return (values, values.FirstOrDefault(v => v.Shape is UnknownShape));
    }

    private static EquatableArray<IrNullReason> Reasons(IEnumerable<Value> values) =>
        values.Select(v => v.NullableBecause).Aggregate((EquatableArray<IrNullReason>)[], NullReasons.Join);

    // ---- dates ------------------------------------------------------------------------------------------------------

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
    private Value DateDiff(FunctionCall call, ScalarType type, BindingScope scope)
    {
        var (values, unknown) = Arguments(call.Parameters.Skip(1), scope);
        if (unknown is not null)
            return unknown;
        if (values.Select((v, i) => DateOperand(v, call.Parameters[i + 1])).Any(t => t is null))
            return NotModelled(call, "an argument of this type is not modelled as a date.");
        return Typed(type, Reasons(values), call);
    }

    /// <summary>
    /// <c>DATEADD(part, n, d)</c>: d's type, or datetime for a string or an integer; NULL when n or d is. An overflow is an
    /// error (517) under any session options.
    /// </summary>
    private Value DateAdd(FunctionCall call, BindingScope scope)
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
        return Typed(date.IsDateOrTime ? date : ScalarType.Of(BuiltInType.DateTime), Reasons(values), call);
    }

    /// <summary><c>DATEPART</c>, <c>YEAR</c>, <c>MONTH</c>, <c>DAY</c> (int) and <c>DATENAME</c> (nvarchar(30), whose names
    /// depend on the session's language, so no closed set): NULL when the date is.</summary>
    private Value DatePart(FunctionCall call, ScalarExpression date, ScalarType type, BindingScope scope)
    {
        var value = Expression(date, scope);
        if (value.Shape is UnknownShape)
            return value;
        if (DateOperand(value, date) is null)
            return NotModelled(call, "a date of this type is not modelled.");
        return Typed(type, value.NullableBecause, call);
    }

    /// <summary>
    /// <c>EOMONTH(d [, n])</c>: a date, NULL when d is. A NULL n is taken as 0, so it does not make it NULL (observed:
    /// A_date_function_is_null_when_an_argument_is_but_eomonth_ignores_a_null_offset).
    /// </summary>
    private Value EndOfMonth(FunctionCall call, BindingScope scope)
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
    private Value FromParts(FunctionCall call, ScalarType type, BindingScope scope, bool precision = false)
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

    // ---- strings ------------------------------------------------------------------------------------------------------

    /// <summary>A value's string type, when it is a string.</summary>
    private static ScalarType? StringOf(Value value) =>
        TypeOf(value) is { IsString: true } type ? type : null;

    /// <summary>
    /// <c>LTRIM</c>, <c>RTRIM</c>, <c>TRIM</c>, <c>UPPER</c> and <c>LOWER</c>: the string's length, varying (char becomes
    /// varchar), max staying max; with characters to trim, nvarchar if either is. NULL when an argument is (observed:
    /// Trimming_and_case_keep_the_strings_length_and_make_a_fixed_length_string_varying). The values change, so no CHECK set holds.
    /// </summary>
    private Value Trim(FunctionCall call, ScalarExpression text, ScalarExpression? characters, BindingScope scope)
    {
        var (values, unknown) = Arguments(characters is null ? [text] : [text, characters], scope);
        if (unknown is not null)
            return unknown;
        if (StringOf(values[0]) is not { } type || (characters is not null && StringOf(values[1]) is null))
            return NotModelled(call, "an argument that is not a string is not modelled.");
        var national = type.IsNational || (characters is not null && StringOf(values[1])!.IsNational);
        return Typed(ScalarType.CharacterString(national, varying: true, type.Length), Reasons(values), call);
    }

    /// <summary>
    /// <c>LEFT</c>, <c>RIGHT</c> and <c>SUBSTRING</c>: the string's type made varying, as long as an integer literal length when
    /// that is shorter (at least 1), max staying max; NULL when an argument is; past the end it is <c>''</c> (observed:
    /// Left_right_and_substring_are_as_long_as_a_literal_length_allows). A length the server folds (<c>1 + 1</c>) is not
    /// modelled, one reading a column or variable is the string's length.
    /// </summary>
    private Value Part(TSqlFragment call, ScalarExpression text, IReadOnlyList<ScalarExpression> others, ScalarExpression length, BindingScope scope)
    {
        var (values, unknown) = Arguments([text, .. others], scope);
        if (unknown is not null)
            return unknown;
        if (StringOf(values[0]) is not { } type)
            return NotModelled(call, "a string function of a value that is not a string is not modelled.");

        var n = type.Length;
        if (!type.IsMax)
        {
            if (LiteralOf(length) is ({ } literal and IntegerLiteral, var negative))
            {
                // A negative length is an error (536); 0 gives '' of length 1.
                if (negative || !long.TryParse(literal.Value, out var given))
                    return NotModelled(call, "a negative length is an error.");
                n = (int)Math.Clamp(given, 1, type.Length);
            }
            else if (!ReadsData(length))
            {
                return NotModelled(call, "a length the server folds from constants is not modelled.");
            }
        }

        return Typed(ScalarType.CharacterString(type.IsNational, varying: true, n), Reasons(values), call);
    }

    /// <summary>Whether an expression reads a column, a variable or a subquery, so that the server cannot fold it.</summary>
    private static bool ReadsData(ScalarExpression expression)
    {
        var finder = new DataFinder();
        expression.Accept(finder);
        return finder.Found;
    }

    private sealed class DataFinder : TSqlFragmentVisitor
    {
        public bool Found { get; private set; }

        public override void Visit(ColumnReferenceExpression node) => Found = true;

        public override void Visit(VariableReference node) => Found = true;

        public override void Visit(ScalarSubquery node) => Found = true;
    }

    /// <summary>
    /// <c>LEN</c> of a string and <c>DATALENGTH</c> of any scalar: int, bigint over max (observed:
    /// Len_and_datalength_are_int_or_bigint_over_max_and_len_ignores_trailing_spaces). NULL when its argument is.
    /// </summary>
    private Value Length(FunctionCall call, ScalarExpression argument, BindingScope scope, bool bytes)
    {
        var value = Expression(argument, scope);
        if (value.Shape is UnknownShape)
            return value;
        var type = value.IsLiveJson ? null : TypeOf(value);
        if (type is null || (!bytes && !type.IsString))
            return NotModelled(call, "a length of a value of this type is not modelled.");
        return Typed(ScalarType.Of(type.IsMax ? BuiltInType.BigInt : BuiltInType.Int), value.NullableBecause, call);
    }

    /// <summary>
    /// <c>REPLACE(s, p, r)</c>: varchar(8000), nvarchar(4000) if any argument is national, max only when s is (observed:
    /// Replace_widens_to_8000_bytes_unless_its_string_is_max). NULL when any argument is.
    /// </summary>
    private Value Replace(FunctionCall call, BindingScope scope)
    {
        var (values, unknown) = Arguments(call.Parameters, scope);
        if (unknown is not null)
            return unknown;
        var types = values.Select(StringOf).ToList();
        if (types.Any(t => t is null))
            return NotModelled(call, "REPLACE of a value that is not a string is not modelled.");
        return Typed(ScalarType.CharacterString(types.Any(t => t!.IsNational), varying: true, types[0]!.IsMax ? -1 : BuiltInType.MostBytes), Reasons(values), call);
    }

    /// <summary><c>STRING_ESCAPE(s, 'json')</c>: nvarchar(max), NULL when s is.</summary>
    private Value StringEscape(FunctionCall call, BindingScope scope)
    {
        var value = Expression(call.Parameters[0], scope);
        if (value.Shape is UnknownShape)
            return value;
        if (StringOf(value) is null)
            return NotModelled(call, "STRING_ESCAPE of a value that is not a string is not modelled.");
        return Typed(new ScalarType(BuiltInType.NVarChar, -1), value.NullableBecause, call);
    }

    /// <summary>
    /// <c>FORMATMESSAGE(format, …)</c> with a string format: nvarchar(4000), NULL only when the format is; a NULL or missing
    /// argument is written <c>(null)</c>, and a bad conversion gives terse error text (observed:
    /// String_escape_is_nvarchar_max_and_formatmessage_nvarchar_4000_writing_a_null_argument_as_null_in_brackets). A message
    /// number names a message the project does not hold.
    /// </summary>
    private Value FormatMessage(FunctionCall call, BindingScope scope)
    {
        var format = Expression(call.Parameters[0], scope);
        if (format.Shape is UnknownShape)
            return format;
        if (StringOf(format) is null)
            return NotModelled(call, "FORMATMESSAGE of a message number is not modelled.");
        return Typed(new ScalarType(BuiltInType.NVarChar, 4000), format.NullableBecause, call);
    }

    /// <summary>
    /// <c>JSON_VALUE(doc, path)</c>: nvarchar(4000), NULL for a JSON null under any path, and under a lax path for a missing
    /// property, an object or array, or a string longer than 4000 (observed:
    /// Json_value_is_nvarchar_4000_and_null_for_a_json_null_a_missing_path_an_object_or_a_long_string), so always
    /// <c>json-null-or-absent</c>, which covers a NULL document too. Neither argument changes the type.
    /// </summary>
    private Value JsonValue(FunctionCall call) =>
        Typed(new ScalarType(BuiltInType.NVarChar, 4000), [new IrNullReason(IrNullRule.JsonNullOrAbsent, At(call).ToString())], call);

    // ---- STRING_AGG -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>STRING_AGG(x, sep)</c>: varchar(8000) for a varchar or char, nvarchar(4000) for an nvarchar or anything not a string,
    /// max over max; passing the limit is an error (9829) under any session options. NULLs are skipped, so it is NULL when every
    /// value is, and over no rows without GROUP BY (observed: String_agg_is_8000_bytes_unless_max_and_null_over_no_rows_or_only_nulls,
    /// String_agg_past_its_limit_is_an_error_even_under_the_callers_ansi_warnings_and_arithabort_off). The separator, even NULL,
    /// changes neither.
    /// </summary>
    private Value StringAggregate(FunctionCall call, BindingScope scope)
    {
        var argument = InAggregate(() => Expression(call.Parameters[0], scope));
        if (argument.Shape is UnknownShape)
            return argument;
        if (argument.IsLiveJson || TypeOf(argument) is not { } from)
            return NotModelled(call, "STRING_AGG of a value of this type is not modelled.");

        // Refused when the procedure is created (8116; observed: Json_value_eomonth_string_agg_and_percentiles_refuse_arguments_they_cannot_read).
        if (from.Type == BuiltInType.UniqueIdentifier)
            return NotModelled(call, "SQL Server refuses STRING_AGG of uniqueidentifier.");
        var type = from.IsString
            ? ScalarType.CharacterString(from.IsNational, varying: true, from.IsMax ? -1 : BuiltInType.MostBytes)
            : ScalarType.CharacterString(national: true, varying: true, BuiltInType.MostBytes);
        return Typed(type, ProvedAggregate(call) ? [] : [.. argument.NullableBecause, .. Aggregate(call)], call);
    }

    // ---- GENERATE_SERIES ------------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>GENERATE_SERIES(start, stop [, step])</c> in FROM: one column, <c>value</c>, never null, of its arguments' one integer
    /// type, or of the type COALESCE gives decimals all named decimal or all numeric; any other mix, decimal with numeric
    /// included, is an error (5373). A NULL argument, or a step the wrong way,
    /// gives no rows (observed: Generate_series_gives_value_of_its_arguments_type_never_null_and_no_rows_for_a_null_argument).
    /// </summary>
    private LocalRelationValues GenerateSeries(GlobalFunctionTableReference reference, BindingScope? outer)
    {
        var at = At(reference);
        var scope = binder.BindFrom(new QuerySpecification(), outer, outer is null ? locals : null);
        var arguments = reference.Parameters.ToList();
        if (arguments.Count is not (2 or 3))
            return LocalRelationValues.Unknown(Unsupported(IrUnknownReason.UnsupportedSource, "GENERATE_SERIES takes two or three arguments.", at));

        var (values, unknown) = Arguments(arguments, scope);
        if (unknown is not null)
            return LocalRelationValues.Unknown((UnknownShape)unknown.Shape);
        var types = values.Select((v, i) => v.IsLiveJson ? null : TypeOf(v)).ToList();
        ScalarType? type = null;
        if (types.Count > 0 && types.All(t => t is { IsInteger: true }) && types.Select(t => t!.Type).Distinct().Count() == 1)
            type = types[0];
        else if (types.Count > 0 && types.All(t => t is { IsDecimal: true }) && types.Select(t => t!.Type).Distinct().Count() == 1)
            // Decimals: each counts its own precision and scale, so every operand counts its type's.
            type = TypeChain.Of([.. types.Select(t => Operand.OfType(t!))]) is { } common && types.All(t => Conversions.Converts(Operand.OfType(t!), common)) ? common : null;
        if (type is null)
            return LocalRelationValues.Unknown(Unsupported(IrUnknownReason.UnsupportedSource, $"{Text(reference)}: its arguments must be of one integer type, or decimals.", at));
        return new LocalRelationValues(["value"], [Typed(type, [], reference)], null);
    }
}
