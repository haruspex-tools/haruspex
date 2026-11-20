using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

// JSON assembled as text: what each string's text is made of (Value.Pieces), stated by the rules that build strings, and read by
// JsonAssembly where the text is read as JSON. Every rule was observed on SQL Server 2025 RTM-CU10 (JsonAssembledObservationTests
// in the differential tests). See README.md, "JSON assembled as text".
internal sealed partial class Typer
{
    /// <summary>
    /// The pieces of a value's text: its own, when a rule stated them; one JSON value for live JSON; text that can't hold a
    /// quotation mark, a backslash or a control character for a string whose every value is known and holds none; otherwise text
    /// that may be anything.
    /// </summary>
    private EquatableArray<TextPiece> PiecesOf(Value value, ScalarExpression expression)
    {
        if (value.Pieces is { } pieces)
            return pieces;
        if (value.IsLiveJson && value.Shape is not UnknownShape)
            return [new JsonPiece(value.Shape)];
        if (!value.IsLiveJson && value.Shape is ScalarShape { Type.Facets.Values: { Match: IrValueMatch.Exact } set } && set.Items.All(v => v.IsString && v.Text.All(NeedsNoEscape)))
            return [new SafeTextPiece(value.Shape, $"{Text(expression)} is written into the brackets as text, outside quotes", At(expression))];
        return [new OpaquePiece(Text(expression), At(expression))];
    }

    /// <summary>Whether a character needs no escape in a JSON string: not a quotation mark, a backslash or a control character.</summary>
    private static bool NeedsNoEscape(char c) => c is not ('"' or '\\') and >= ' ';

    /// <summary>
    /// The pieces of an operand that may be NULL where NULL counts as empty: those of its text, or none at all. In <c>concat</c>
    /// always, and in <c>+</c> unless the callers' sessions are declared to set <c>CONCAT_NULL_YIELDS_NULL</c> on, under which NULL
    /// makes the whole NULL (observed: With_concat_null_yields_null_off_plus_over_a_null_aggregate_is_empty_brackets).
    /// </summary>
    private static EquatableArray<TextPiece> OrEmpty(EquatableArray<TextPiece> pieces, Value value, bool nullIsEmpty) =>
        nullIsEmpty && value.NullableBecause.Count > 0 ? [new EitherPiece([pieces, []])] : pieces;

    /// <summary>
    /// How text may be cut short at its end: at the most a type holds (true), by a conversion (false), or not at all (null).
    /// A cut is only ever at the end of a value's pieces (<see cref="Joined"/>).
    /// </summary>
    private static bool? CutAtEnd(EquatableArray<TextPiece> pieces) => pieces.Count == 0 ? null : pieces[^1] switch
    {
        CutPiece cut => cut.AtTypeLimit,
        EitherPiece either => either.Alternatives.Select(CutAtEnd).Aggregate((bool?)null, (a, b) => a == false || b == false ? false : a ?? b),
        _ => null,
    };

    /// <summary>
    /// The pieces of text joined from operands, by <c>+</c> or <c>concat</c>: each operand's in order. An operand that may have
    /// been cut short is followed by text that would come after the cut, so it is read as text that may be anything, unless it was
    /// cut at the most its type holds and the result is not <c>max</c> either, which cuts what follows away too (<see cref="CutPiece"/>).
    /// </summary>
    /// <param name="operands">Each operand's pieces and its expression.</param>
    /// <param name="max">Whether the result is <c>max</c>, which is never cut.</param>
    /// <param name="mayBeCut">Whether the result may be longer than its type holds, so cut short at the end.</param>
    private EquatableArray<TextPiece> Joined(IReadOnlyList<(EquatableArray<TextPiece> Pieces, ScalarExpression Expression)> operands, bool max, bool mayBeCut)
    {
        var pieces = new List<TextPiece>();
        for (var i = 0; i < operands.Count; i++)
        {
            var (operand, expression) = operands[i];
            pieces.AddRange(i < operands.Count - 1 && CutAtEnd(operand) is { } atLimit && (max || !atLimit)
                ? [new OpaquePiece($"{Text(expression)}, which may be cut short,", At(expression))]
                : operand);
        }

        return mayBeCut ? [new CutPiece([.. pieces], AtTypeLimit: true)] : [.. pieces];
    }

    /// <summary>
    /// An item of <c>string_agg</c> or <c>concat_ws</c>: its pieces, unless it may be cut short, which would leave its text
    /// unfinished before the separator, so it is read as text that may be anything.
    /// </summary>
    private EquatableArray<TextPiece> Whole(EquatableArray<TextPiece> pieces, ScalarExpression expression) =>
        CutAtEnd(pieces) is null ? pieces : [new OpaquePiece($"{Text(expression)}, which may be cut short,", At(expression))];

    /// <summary>
    /// What a value converted to a string type is as text: a string's own pieces, cut short when the type is shorter (and none
    /// from <c>nvarchar</c> into <c>varchar</c>, whose code page may turn a character into a quotation mark); a number in a form that is always a
    /// JSON number of its value (observed: A_number_converted_to_text_is_written_so); any other number, a date, a time or a
    /// <c>uniqueidentifier</c> as text that needs no escape inside quotes. Null for anything else.
    /// </summary>
    /// <param name="value">The value converted.</param>
    /// <param name="to">The string type it is converted to.</param>
    /// <param name="converted">The converted value's shape.</param>
    /// <param name="call">The conversion, whose style it reads.</param>
    private EquatableArray<TextPiece>? ConvertedPieces(Value value, ScalarType to, Shape converted, TSqlFragment call)
    {
        if (!to.IsString || value.IsLiveJson || TypeOf(value) is not { } from || value.Shape is not ScalarShape scalar)
            return null;
        var at = At(call);
        if (from.IsString)
        {
            // Into varchar from nvarchar, a character the code page lacks may become a quotation mark or a backslash.
            if (value.Pieces is not { } pieces || (from.IsNational && !to.IsNational))
                return null;
            return to.IsMax || (!from.IsMax && to.Length >= from.Length) ? pieces : [new CutPiece(pieces, AtTypeLimit: false)];
        }

        var style = call switch
        {
            ConvertCall { Style: { } written } => LiteralOf(written) is ({ } literal and IntegerLiteral, false) ? literal.Value : "?",
            TryConvertCall { Style: { } written } => LiteralOf(written) is ({ } literal and IntegerLiteral, false) ? literal.Value : "?",
            _ => null,
        };
        var plain = style is null or "0";
        Shape? number = from switch
        {
            { IsInteger: true } or { IsDecimal: true } when plain => scalar,
            { IsMoney: true } when style is "2" or "126" => scalar,
            { IsBit: true } when plain => BitAsNumber(at),
            _ => null,
        };
        if (number is not null)
            return [new NumberPiece(number, converted)];
        if (from.IsNumber || from.IsBit || from.IsDateOrTime || from.Family == TypeFamily.Uuid)
            return [new SafeTextPiece(converted, $"{Text(call)} is written into the brackets as text that is not a JSON number of its value", at)];
        return null;
    }

    /// <summary>A <c>bit</c> converted to text: <c>0</c> or <c>1</c>, a JSON number, where FOR JSON writes <c>false</c> or <c>true</c>.</summary>
    private static ScalarShape BitAsNumber(SourceLocation at) => new(
        new IrScalar(IrScalarKind.Number, null, null, new IrFacets(Values: new IrValueSet(
            [new IrValue("0", IsString: false), new IrValue("1", IsString: false)],
            IrValueMatch.Exact,
            [new IrFactSource(IrFactSourceKind.Expression, at.ToString())]))),
        null);

    /// <summary>
    /// The string type live JSON is as text beside other strings in <c>+</c> and <c>concat</c>: <c>nvarchar(max)</c> when it is built
    /// here as text (<see cref="BuiltAsText"/>), whose text is the JSON (observed:
    /// A_null_fragment_in_concat_leaves_a_stray_comma_which_json_query_refuses_unless_it_is_the_only_one); null for anything
    /// else, such as a <c>json</c> value, which SQL Server refuses to concatenate (257).
    /// </summary>
    private ScalarType? LiveText(Value value, ScalarExpression expression) =>
        value.IsLiveJson && value.Shape is not UnknownShape && LiveJsonType(expression) is { IsMax: true } type && type.Type == BuiltInType.NVarChar ? type : null;

    /// <summary>
    /// The pieces of <c>concat(a, b, …)</c>, a NULL argument counting as empty, or of <c>concat_ws(separator, a, b, …)</c>, a NULL
    /// argument left out with its separator (<see cref="JoinedPiece"/>). An integer or a <c>bit</c> is written as its number
    /// (observed: A_number_converted_to_text_is_written_so).
    /// </summary>
    /// <param name="arguments">The arguments, the separator first for <c>concat_ws</c>.</param>
    /// <param name="values">Their values, null for the NULL literal.</param>
    private EquatableArray<TextPiece> ConcatPieces(List<ScalarExpression> arguments, List<Value?> values, bool separated, bool max, bool mayBeCut)
    {
        EquatableArray<TextPiece> Of(int i) => values[i] is not { } value
            ? []
            : value.Pieces is null && !value.IsLiveJson && TypeOf(value) is { } type && value.Shape is ScalarShape scalar && (type.IsBit || type.Type == BuiltInType.Int)
                ? [new NumberPiece(type.IsBit ? BitAsNumber(At(arguments[i])) : scalar, AnyText)]
                : PiecesOf(value, arguments[i]);

        if (!separated)
            return Joined([.. arguments.Select((a, i) => (values[i] is { } v ? OrEmpty(Of(i), v, nullIsEmpty: true) : [], a))], max, mayBeCut);

        // A NULL separator is no text; a NULL argument is left out, with its separator.
        var items = Enumerable.Range(1, arguments.Count - 1)
            .Where(i => values[i] is not null)
            .Select(i => new JoinedPiece.Item(Whole(Of(i), arguments[i]), values[i] is { NullableBecause.Count: > 0 }))
            .ToList();
        var separator = values[0] is { } sep ? Whole(OrEmpty(Of(0), sep, nullIsEmpty: true), arguments[0]) : [];
        EquatableArray<TextPiece> joined = [new JoinedPiece(separator, [.. items])];
        return mayBeCut ? [new CutPiece(joined, AtTypeLimit: true)] : joined;
    }

    /// <summary>A number's text as a string: digits, a sign and a point, with no facet claimed.</summary>
    private static ScalarShape AnyText { get; } = new(new IrScalar(IrScalarKind.String, null, null), null);

    /// <summary>
    /// The pieces of an item of <c>string_agg</c>: a number is converted to text as <c>convert</c> with no style writes it, which is
    /// a JSON number of its value for an integer, a decimal and a <c>bit</c> (observed: A_number_converted_to_text_is_written_so).
    /// </summary>
    private EquatableArray<TextPiece> ItemPieces(Value item, ScalarExpression expression) =>
        item.Pieces is null && !item.IsLiveJson && TypeOf(item) is { IsString: false }
            ? ConvertedPieces(item, new ScalarType(BuiltInType.NVarChar, -1), AnyText, expression) ?? PiecesOf(item, expression)
            : PiecesOf(item, expression);

    /// <summary>The pieces of a separator: a literal's text; anything else, even NULL (which glues the items together), may be anything.</summary>
    private EquatableArray<TextPiece> SeparatorPieces(ScalarExpression separator) =>
        LiteralOf(separator) is ({ } literal and StringLiteral, false)
            ? [new LiteralPiece(literal.Value, ((StringLiteral)literal).IsNational, At(literal))]
            : [new OpaquePiece(Text(separator), At(separator))];

    /// <summary>
    /// The pieces of <c>COALESCE</c>, or of the results of a <c>CASE</c>, of string type: those of any argument it may give, each up
    /// to the first that is never NULL, converted to the common type (<see cref="ConvertedPieces"/>); none when that type is not a
    /// string.
    /// </summary>
    /// <param name="expressions">The arguments or results, in order, none of them the NULL literal.</param>
    /// <param name="values">Their values.</param>
    /// <param name="type">The common type.</param>
    /// <param name="first">Whether only those up to the first never NULL are given, as for <c>COALESCE</c>.</param>
    private EquatableArray<TextPiece>? EitherPieces(List<ScalarExpression> expressions, List<Value> values, ScalarType type, bool first = true)
    {
        if (!type.IsString)
            return null;
        var alternatives = new List<EquatableArray<TextPiece>>();
        for (var i = 0; i < values.Count; i++)
        {
            if (TypeOf(values[i]) is not { IsString: true })
                return null;
            alternatives.Add(values[i].Pieces is null
                ? PiecesOf(values[i], expressions[i])
                : ConvertedPieces(values[i], type, AnyText, expressions[i]) ?? [new OpaquePiece(Text(expressions[i]), At(expressions[i]))]);
            if (first && values[i].NullableBecause.Count == 0)
                break;
        }

        return [new EitherPiece([.. alternatives])];
    }

    /// <summary>
    /// The JSON a string holds when it is assembled here as text (<see cref="JsonAssembly"/>): its shape, or an unknown saying why
    /// it is not known; null when it is not this idiom.
    /// </summary>
    /// <param name="value">The string.</param>
    /// <param name="at">Where it is read.</param>
    internal static Shape? Assembled(Value value, SourceLocation at) =>
        !value.IsLiveJson && value.Pieces is { } pieces ? JsonAssembly.Parse(pieces, at) : null;

    /// <summary>
    /// <see cref="Assembled(Value, SourceLocation)"/> of an expression read as JSON, recording the place for the note
    /// (<see cref="Summaries.Assembled"/>) when it is this idiom.
    /// </summary>
    private Shape? Assembled(Value value, ScalarExpression read)
    {
        if (value.IsLiveJson || value.Pieces is not { } pieces)
            return null;
        var assembled = JsonAssembly.Parse(pieces, At(read), out var built);
        if (assembled is not null && built)
            summaries.Assembled(At(Strip(read)), Strip(read), JsonAssembly.Aggregates(pieces));
        return assembled;
    }

    /// <summary>Whether an operand of string <c>+</c> is itself a <c>+</c>, whose pieces are its own operands'.</summary>
    private static bool IsSum(ScalarExpression operand) => Strip(operand) is BinaryExpression { BinaryExpressionType: BinaryExpressionType.Add };
}
