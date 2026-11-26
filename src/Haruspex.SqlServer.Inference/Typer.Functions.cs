using Haruspex.Collections;
using Haruspex.Ir;
using Haruspex.SqlServer.Inference.Lattice;
using Haruspex.SqlServer.Inference.Typing;
using Haruspex.SqlServer.Semantics;
using Haruspex.SqlServer.Syntax;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

// The string functions that keep or cut a string, JSON_VALUE, STRING_AGG and GENERATE_SERIES; the date functions are in
// Typer.DateFunctions.cs. Types come from ScalarType.cs; README.md
// in this folder ("Built-in functions") gives the rules and the observations they rest on (FunctionObservationTests in the
// differential tests). Window functions are in Typer.Windows.cs.
internal sealed partial class Typer
{
    /// <summary>
    /// <c>JSON_VALUE</c> of a document the server refuses when the procedure is created: a value that isn't text (8116), or a
    /// string literal that isn't a JSON object or array (13609); null for any other (observed:
    /// Json_value_eomonth_string_agg_and_percentiles_refuse_arguments_they_cannot_read).
    /// </summary>
    private Value? RefusedJsonDocument(FunctionCall call, ReadScope scope)
    {
        var document = call.Parameters[0];
        if (Strip(document) is StringLiteral literal && !SqlcmdVariableReference.IsIn(literal))
            return JsonLiterals.IsDocumentText(literal.Value) ? null : NotModelled(call, "SQL Server refuses it: its document is not a JSON object or array.");
        var value = Expression(document, scope);
        return !value.IsLiveJson && value.Shape is ScalarShape && TypeOf(value) is { IsString: false } type
            ? NotModelled(call, $"SQL Server refuses JSON_VALUE of {type.Text}.")
            : null;
    }

    /// <summary>Reads arguments, giving the first that is unknown, if any.</summary>
    private (List<Value> Values, Value? Unknown) Arguments(IEnumerable<ScalarExpression> arguments, ReadScope scope)
    {
        var values = arguments.Select(a => Expression(a, scope)).ToList();
        return (values, values.FirstOrDefault(v => v.Shape is UnknownShape));
    }

    private static EquatableArray<IrNullReason> Reasons(IEnumerable<Value> values) =>
        values.Select(v => v.NullableBecause).Aggregate((EquatableArray<IrNullReason>)[], NullReasons.Join);

    // ---- strings ------------------------------------------------------------------------------------------------------

    /// <summary>A value's string type, when it is a string.</summary>
    private static ScalarType? StringOf(Value value) =>
        TypeOf(value) is { IsString: true } type ? type : null;

    /// <summary>
    /// <c>LTRIM</c>, <c>RTRIM</c>, <c>TRIM</c>, <c>UPPER</c> and <c>LOWER</c>: the string's length, varying (char becomes
    /// varchar), max staying max; with characters to trim, nvarchar if either is. NULL when an argument is (observed:
    /// Trimming_and_case_keep_the_strings_length_and_make_a_fixed_length_string_varying). The values change, so no CHECK set holds.
    /// </summary>
    private Value Trim(FunctionCall call, ScalarExpression text, ScalarExpression? characters, ReadScope scope)
    {
        var (values, unknown) = Arguments(characters is null ? [text] : [text, characters], scope);
        if (unknown is not null)
            return unknown;
        if (StringOf(values[0]) is not { } type || (characters is not null && StringOf(values[1]) is null))
            return NotModelled(call, "an argument that is not a string is not modelled.");
        var national = type.IsNational || (characters is not null && StringOf(values[1]) is { IsNational: true });
        return Typed(ScalarType.CharacterString(national, varying: true, type.Length), Reasons(values), call);
    }

    /// <summary>
    /// <c>LEFT</c>, <c>RIGHT</c> and <c>SUBSTRING</c>: the string's type made varying, as long as an integer literal length when
    /// that is shorter (at least 1), max staying max; NULL when an argument is; past the end it is <c>''</c> (observed:
    /// Left_right_and_substring_are_as_long_as_a_literal_length_allows). A length the server folds (<c>1 + 1</c>) is not
    /// modelled, one reading a column or variable is the string's length. A start or a length wider than int may overflow. Of a
    /// binary value, by the same rule, <c>SUBSTRING</c> is varbinary, and <c>LEFT</c> and <c>RIGHT</c>, which convert it to
    /// varchar first, are varchar (observed:
    /// Substring_of_a_binary_value_is_varbinary_and_left_and_right_varchar_as_long_as_a_literal_length_allows).
    /// </summary>
    private Value Part(TSqlFragment call, ScalarExpression text, IReadOnlyList<ScalarExpression> others, ScalarExpression length, ReadScope scope)
    {
        var (values, unknown) = Arguments([text, .. others], scope);
        if (unknown is not null)
            return unknown;
        if ((StringOf(values[0]) ?? (TypeOf(values[0]) is { IsBinary: true } bytes ? bytes : null)) is not { } type)
            return NotModelled(call, "a string function of a value that is not a string or binary is not modelled.");

        // SUBSTRING's start and length must be an integer or a decimal; LEFT's and RIGHT's length must convert to int implicitly.
        // SQL Server refuses any other when the procedure is created (8116, 206, 260; observed:
        // A_start_or_length_past_int_overflows_in_charindex_substring_left_and_right_and_one_not_a_number_is_refused).
        bool Read(ScalarType? t) => t is not null && (call is FunctionCall
            ? t.IsInteger || t.IsDecimal
            : ConversionChart.Of(t.Type, BuiltInType.Int) == ConversionKind.Implicit);
        if (others.Select((e, i) => Strip(e) is NullLiteral || Read(TypeOf(values[i + 1]))).Any(ok => !ok))
            return NotModelled(call, "SQL Server refuses a start or a length of this type.");

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

        // A start or a length wider than int may overflow it (FitsInt).
        var overflows = others.Select((e, i) => !FitsInt(values[i + 1], e)).Any(o => o);
        var part = type.IsBinary && call is FunctionCall ? ScalarType.ByteString(varying: true, n) : ScalarType.CharacterString(type.IsNational, varying: true, n);
        return Typed(part, overflows ? NullReasons.Join(Reasons(values), Overflow(call)) : Reasons(values), call);
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
    private Value Length(FunctionCall call, ScalarExpression argument, ReadScope scope, bool bytes)
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
    private Value Replace(FunctionCall call, ReadScope scope)
    {
        var (values, unknown) = Arguments(call.Parameters, scope);
        if (unknown is not null)
            return unknown;
        var types = values.Select(StringOf).ToList();
        if (types.Any(t => t is null))
            return NotModelled(call, "REPLACE of a value that is not a string is not modelled.");
        var strings = types.OfType<ScalarType>().ToList();
        return Typed(ScalarType.CharacterString(strings.Any(t => t.IsNational), varying: true, strings[0].IsMax ? -1 : BuiltInType.MostBytes), Reasons(values), call);
    }

    /// <summary><c>STRING_ESCAPE(s, 'json')</c>: nvarchar(max), NULL when s is.</summary>
    private Value StringEscape(FunctionCall call, ReadScope scope)
    {
        var value = Expression(call.Parameters[0], scope);
        if (value.Shape is UnknownShape)
            return value;
        if (StringOf(value) is null)
            return NotModelled(call, "STRING_ESCAPE of a value that is not a string is not modelled.");
        var escaped = Typed(new ScalarType(BuiltInType.NVarChar, -1), value.NullableBecause, call);

        // Escaped for JSON, its text needs no escape inside quotes, and a JSON reader gets the value back from it (observed:
        // An_unescaped_value_in_quotes_can_make_more_elements_a_number_or_cut_the_array_short). 'json' is the only type SQL
        // Server accepts.
        return LiteralOf(call.Parameters[1]) is ({ } kind and StringLiteral, false) && string.Equals(kind.Value, "json", StringComparison.OrdinalIgnoreCase)
            ? escaped with { Digits = escaped.Digits, Pieces = [new SafeTextPiece(value.Shape, $"{Text(call)} is written into the brackets outside quotes, where it may be any text", At(call))] }
            : escaped;
    }

    /// <summary>
    /// <c>FORMATMESSAGE(format, …)</c> with a string format: nvarchar(4000), NULL only when the format is; a NULL or missing
    /// argument is written <c>(null)</c>, and a bad conversion gives terse error text (observed:
    /// String_escape_is_nvarchar_max_and_formatmessage_nvarchar_4000_writing_a_null_argument_as_null_in_brackets). A message
    /// number names a message the project does not hold.
    /// </summary>
    private Value FormatMessage(FunctionCall call, ReadScope scope)
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
    private Value StringAggregate(FunctionCall call, ReadScope scope)
    {
        var argument = Expression(call.Parameters[0], scope.InAggregate());
        if (argument.Shape is UnknownShape)
            return argument;
        if ((LiveText(argument, call.Parameters[0]) ?? (argument.IsLiveJson ? null : TypeOf(argument))) is not { } from)
            return NotModelled(call, "STRING_AGG of a value of this type is not modelled.");

        // Refused when the procedure is created (8116; observed: Json_value_eomonth_string_agg_and_percentiles_refuse_arguments_they_cannot_read).
        if (from.Type == BuiltInType.UniqueIdentifier)
            return NotModelled(call, "SQL Server refuses STRING_AGG of uniqueidentifier.");

        // Refused too (8116, observed for binary and varbinary over the catalogue sweep, CatalogueSweepGridTests), and not modelled
        // for the other binary values.
        if (from.IsBinary || from.Family == TypeFamily.RowVersion || from.Type == BuiltInType.Image)
            return NotModelled(call, "SQL Server refuses STRING_AGG of a binary value.");
        if (from.Type == BuiltInType.Xml)
            return NotModelled(call, "SQL Server refuses STRING_AGG of xml (8116; observed over the catalogue sweep, CatalogueSweepGridTests).");
        var type = from.IsString
            ? ScalarType.CharacterString(from.IsNational, varying: true, from.IsMax ? -1 : BuiltInType.MostBytes)
            : ScalarType.CharacterString(national: true, varying: true, BuiltInType.MostBytes);
        var aggregated = Typed(type, ProvedAggregate(call, scope) ? [] : [.. argument.NullableBecause, .. Aggregate(call, scope)], call);

        // Past its limit it is an error, never cut short, so its text is every item's (Typer.JsonAssembled.cs).
        return aggregated with { Digits = aggregated.Digits, Pieces = [new ListPiece(Whole(ItemPieces(argument, call.Parameters[0]), call.Parameters[0]), SeparatorPieces(call.Parameters[1]))] };
    }

    // ---- GENERATE_SERIES ------------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>GENERATE_SERIES(start, stop [, step])</c> in FROM: one column, <c>value</c>, never null, of its arguments' one integer
    /// type, or of the type COALESCE gives decimals all named decimal or all numeric; any other mix, decimal with numeric
    /// included, is an error (5373). A NULL argument, or a step the wrong way,
    /// gives no rows (observed: Generate_series_gives_value_of_its_arguments_type_never_null_and_no_rows_for_a_null_argument).
    /// </summary>
    /// <param name="symbol">The call, bound.</param>
    /// <param name="scope">The scope its arguments are read in.</param>
    private LocalRelationValues GenerateSeries(SeriesCall symbol, ReadScope scope)
    {
        var reference = symbol.Syntax;
        var at = At(reference);
        var arguments = reference.Parameters.ToList();
        if (arguments.Count is not (2 or 3))
            return LocalRelationValues.Unknown(Unsupported(IrUnknownReason.UnsupportedSource, "GENERATE_SERIES takes two or three arguments.", at));

        var (values, unknown) = Arguments(arguments, scope);
        if (unknown is not null)
            return LocalRelationValues.Unknown((UnknownShape)unknown.Shape);
        var types = values.Select((v, i) => v.IsLiveJson ? null : TypeOf(v)).ToList();
        ScalarType? type = null;
        if (types.Count > 0 && types.All(t => t is { IsInteger: true }) && types.OfType<ScalarType>().Select(t => t.Type).Distinct().Count() == 1)
            type = types[0];
        else if (types.Count > 0 && types.All(t => t is { IsDecimal: true }) && types.OfType<ScalarType>().Select(t => t.Type).Distinct().Count() == 1)
            // Decimals: each counts its own precision and scale, so every operand counts its type's.
            type = TypeChain.Of([.. types.OfType<ScalarType>().Select(Operand.OfType)]) is { } common && types.OfType<ScalarType>().All(t => Conversions.Converts(Operand.OfType(t), common)) ? common : null;
        if (type is null)
            return LocalRelationValues.Unknown(Unsupported(IrUnknownReason.UnsupportedSource, $"{Text(reference)}: its arguments must be of one integer type, or decimals.", at));
        return new LocalRelationValues(["value"], [Typed(type, [], reference)], null);
    }

    // ---- STRING_SPLIT -----------------------------------------------------------------------------------------------------

    /// <summary>The least compatibility level that has <c>STRING_SPLIT</c>.</summary>
    private const int StringSplitLevel = 130;

    /// <summary>
    /// <c>STRING_SPLIT(s, separator [, 1])</c> in FROM: <c>value</c>, s's type made varying, of its length, max staying max,
    /// <c>nvarchar</c> if the separator is; never null, since a NULL string gives no rows and an empty piece is <c>''</c>. With
    /// the literal 1, <c>ordinal</c> too, a bigint from 1. A NULL separator, or one longer than a character, is an error (observed:
    /// String_split_gives_value_of_its_strings_type_varying_never_null_and_ordinal_only_when_asked). Below compatibility level
    /// 130 SQL Server doesn't find it (observed: String_split_needs_compatibility_level_130_and_has_its_ordinal_from_there), so it
    /// is unknown in a project known to be below; at a level not known it is typed, since wherever it runs it gives these
    /// columns (<c>docs/decisions/0067-a-function-a-compatibility-level-lacks-is-unknown-only-at-a-level-known-to-lack-it.md</c>).
    /// </summary>
    /// <param name="symbol">The call, bound.</param>
    /// <param name="scope">The scope its arguments are read in.</param>
    private LocalRelationValues StringSplit(SplitCall symbol, ReadScope scope)
    {
        var reference = symbol.Syntax;
        var at = At(reference);
        if (model.Settings?.CompatibilityLevel is < StringSplitLevel and var level)
        {
            return LocalRelationValues.Unknown(Unsupported(
                IrUnknownReason.UnsupportedSource,
                $"STRING_SPLIT needs compatibility level {StringSplitLevel}; at the project's, {level}, SQL Server doesn't find it (208).",
                at));
        }

        if (reference.Parameters.Count is not (2 or 3) || symbol.Columns is not { } columns)
            return LocalRelationValues.Unknown(Unsupported(IrUnknownReason.UnsupportedSource, $"{Text(reference)}: STRING_SPLIT is read with two arguments, or three whose last is the literal 0, 1 or NULL.", at));

        var (values, unknown) = Arguments(reference.Parameters.Take(2), scope);
        if (unknown is not null)
            return LocalRelationValues.Unknown((UnknownShape)unknown.Shape);
        if (StringOf(values[0]) is not { } text || StringOf(values[1]) is not { } separator)
            return LocalRelationValues.Unknown(Unsupported(IrUnknownReason.UnsupportedSource, $"{Text(reference)}: STRING_SPLIT of a value that is not a string is not modelled.", at));

        var value = Typed(ScalarType.CharacterString(text.IsNational || separator.IsNational, varying: true, text.IsMax ? -1 : text.Length), [], reference);
        return columns.Count == 1
            ? new LocalRelationValues(columns, [value], null)
            : new LocalRelationValues(columns, [value, Within(Typed(ScalarType.Of(BuiltInType.BigInt), [], reference), 1, long.MaxValue, "STRING_SPLIT")], null);
    }
}
