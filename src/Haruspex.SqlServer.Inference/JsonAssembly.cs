using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Inference.Lattice;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// Which JSON a string assembled as text holds: an array, <c>'[' + string_agg(…) + ']'</c>, <c>concat('[', a, ',', b, ']')</c>,
/// <c>'[' + concat_ws(',', …) + ']'</c>, or an object, <c>'{"id":' + … + ',"status":"' + string_escape(…, 'json') + '"}'</c>,
/// <c>'{' + concat_ws(',', '"id":' + …, …) + '}'</c>, each inside the other across pieces, read from its <see cref="TextPiece"/>s
/// where the text is read as JSON. It claims only what the server guarantees: each value is text that is exactly one JSON value
/// (a literal, a number converted to text, live JSON, an array or object read by these rules) or a string in quotes whose text
/// can't hold a quotation mark, a backslash or a control character, and each key is written whole in a literal, at most once on
/// any way through the text; anything else makes the JSON unknown, saying why
/// (docs/decisions/0084-json-assembled-as-text-is-typed-where-each-element-is-one-json-value.md,
/// docs/decisions/0086-a-json-object-assembled-as-text-is-typed-where-each-key-is-written-once-in-a-literal.md). The README in
/// this folder, "JSON assembled as text", gives the rules and the observations they rest on.
/// </summary>
/// <remarks>
/// <para>
/// <b>Alternatives.</b> Text that is one of several (<see cref="EitherPiece"/>) is read as each, and the JSON is the join of what
/// each holds (<see cref="JsonShapes.Join"/>). Text that is not JSON, such as <c>[,{"a":1}]</c> or <c>{"a":}</c> left by a NULL
/// in <c>concat</c>, is an error where it is read (13609, observed:
/// A_null_fragment_in_concat_leaves_a_stray_comma_which_json_query_refuses_unless_it_is_the_only_one and
/// A_null_value_leaves_an_object_null_under_plus_and_not_json_under_concat), so it describes no call that succeeds and adds
/// nothing.
/// </para>
/// <para>
/// <b>Only the first value is read.</b> <c>json_query</c> reads text as far as its first value and ignores the rest, so a
/// value in quotes that holds <c>"]</c> would end the array early and the read would succeed (observed:
/// An_unescaped_value_in_quotes_can_make_more_elements_a_number_or_cut_the_array_short). That is why a value in quotes must be
/// escaped, and why text after the array or object is not read here: it is unknown, which claims less.
/// </para>
/// <para>
/// <b>A key written twice</b> is kept twice in the text, and readers differ on which value they keep (observed:
/// A_key_written_twice_is_kept_twice_and_readers_differ_on_which_wins): <c>JSON_VALUE</c> the first, <c>JSON.parse</c> the
/// last. So a key that may be written twice on one way through the text makes the object unknown, rather than taking its type
/// from one of the writes.
/// </para>
/// </remarks>
internal static partial class JsonAssembly
{
    /// <summary>The most alternatives read; text with more is not read (it claims nothing).</summary>
    private const int MostAlternatives = 64;

    /// <summary>
    /// The JSON the text holds when it is JSON assembled here: its shape, or an unknown saying why it is not known. Null when the
    /// text is not an array or object assembled from pieces (a plain string, a literal on its own), or when every way it can be
    /// is not JSON, which claims nothing.
    /// </summary>
    /// <param name="pieces">The text's pieces.</param>
    /// <param name="at">Where it is read, for an unknown with no piece of its own to name.</param>
    public static Shape? Parse(EquatableArray<TextPiece> pieces, SourceLocation at) => Parse(pieces, at, out _);

    /// <summary>
    /// <see cref="Parse(EquatableArray{TextPiece}, SourceLocation)"/>, saying too whether these pieces build the JSON as text: open
    /// an array or object across pieces, or join literals into one. Text that is one JSON value whole, such as a variable read
    /// back holding an array assembled where it was written, or <c>FOR JSON</c> text, is JSON here but is not built here, so it
    /// is not noted.
    /// </summary>
    public static Shape? Parse(EquatableArray<TextPiece> pieces, SourceLocation at, out bool built)
    {
        built = false;
        if (Expand(pieces) is not { } alternatives)
            return null;
        Shape? content = null;
        UnknownShape? unknown = null;
        var assembled = false;
        foreach (var raw in alternatives)
        {
            var reader = new Reader(Merged(raw), at, Reader.Mode.Document);
            var read = reader.Read() switch
            {
                Reader.Outcome.Read => reader.Whole,
                Reader.Outcome.NotKnown => reader.Why,
                _ => null,
            };

            // Text joined from more than one piece is assembled, even when its literals join into one JSON value.
            var joined = read is not null and not UnknownShape && raw.Count(p => p is not LiteralPiece { Text.Length: 0 }) > 1;
            assembled |= reader.Assembled || joined;
            built |= reader.Opened || joined;
            if (read is UnknownShape why)
                unknown ??= why;
            else if (read is not null)
                content = content is null ? read : JsonShapes.Join(content, read);
        }

        return !assembled ? null : unknown ?? content;
    }

    /// <summary>Whether text is made with <c>string_agg</c>, anywhere in its pieces: the note then names <c>json_arrayagg</c>.</summary>
    public static bool Aggregates(EquatableArray<TextPiece> pieces) => pieces.Any(piece => piece switch
    {
        ListPiece => true,
        EitherPiece either => either.Alternatives.Any(Aggregates),
        CutPiece cut => Aggregates(cut.Pieces),
        JoinedPiece joined => joined.Items.Any(i => Aggregates(i.Pieces)),
        _ => false,
    });

    /// <summary>
    /// What the text of one item of <c>string_agg</c> or <c>concat_ws</c> may be, read as each of its alternatives.
    /// </summary>
    /// <param name="Shape">The JSON value it is, when it is one: its shape; null when no way it can be is one JSON value.</param>
    /// <param name="Nulls">Why that value may be JSON null.</param>
    /// <param name="MayBeEmpty">
    /// Whether it may be empty text, which writes nothing: alone between the brackets it leaves <c>[]</c>, which is JSON (observed:
    /// An_empty_item_alone_between_the_brackets_leaves_an_empty_array), so it may be absent.
    /// </param>
    /// <param name="EndsOpen">
    /// Whether its text may end in a number or a word, which text written straight after it would continue (observed:
    /// A_number_written_straight_after_a_number_continues_it).
    /// </param>
    private sealed record ItemText(Shape? Shape, EquatableArray<IrNullReason> Nulls, bool MayBeEmpty, bool EndsOpen);

    /// <summary>
    /// What one item of <c>string_agg</c> or <c>concat_ws</c> is (<see cref="ItemText"/>), or an unknown saying why it is not
    /// known: an item whose text may be more than one JSON value, or text this does not read.
    /// </summary>
    private static ItemText? Item(EquatableArray<TextPiece> pieces, SourceLocation at, out UnknownShape? unknown)
    {
        unknown = null;
        if (Expand(pieces) is not { } alternatives)
        {
            unknown = Unknown("an item has too many alternatives to read", at);
            return null;
        }

        Shape? shape = null;
        EquatableArray<IrNullReason> nulls = [];
        var empty = false;
        var open = false;
        foreach (var raw in alternatives)
        {
            var reader = new Reader(Merged(raw), at, Reader.Mode.Value);
            var outcome = reader.Read();
            if (outcome == Reader.Outcome.NotKnown)
            {
                unknown = reader.Why;
                return null;
            }

            empty |= reader.Empty;
            if (outcome == Reader.Outcome.NeverJson || reader.Whole is not { } one)
                continue;
            open |= reader.EndsOpen;
            (shape, nulls) = shape is null ? (one, reader.WholeNulls) : (JsonShapes.Join(shape, one), NullReasons.Join(nulls, reader.WholeNulls));
        }

        return new ItemText(shape, nulls, empty, open);
    }

    /// <summary>
    /// What the text of one item of <c>concat_ws</c> or <c>string_agg</c> written where an object's keys go may be: the members
    /// <c>"key":value</c> it writes, the same keys on every way through it.
    /// </summary>
    /// <param name="Members">Its members, each value joined over the ways through it; none when no way writes any.</param>
    /// <param name="MayBeEmpty">Whether it may be empty text, which writes no member.</param>
    /// <param name="EndsOpen">Whether its text may end in a number or a word, which text written straight after it would continue.</param>
    private sealed record MembersText(EquatableArray<Member> Members, bool MayBeEmpty, bool EndsOpen);

    /// <summary>
    /// What one item written where an object's keys go is (<see cref="MembersText"/>), or an unknown saying why it is not known:
    /// text this does not read, or keys that differ from one way through it to another, which one presence per key can't say.
    /// </summary>
    private static MembersText? MembersItem(EquatableArray<TextPiece> pieces, SourceLocation at, out UnknownShape? unknown)
    {
        unknown = null;
        if (Expand(pieces) is not { } alternatives)
        {
            unknown = Unknown("an item has too many alternatives to read", at);
            return null;
        }

        List<Member>? members = null;
        var empty = false;
        var open = false;
        foreach (var raw in alternatives)
        {
            var reader = new Reader(Merged(raw), at, Reader.Mode.Members);
            var outcome = reader.Read();
            if (outcome == Reader.Outcome.NotKnown)
            {
                unknown = reader.Why;
                return null;
            }

            if (outcome == Reader.Outcome.NeverJson)
                continue;
            if (reader.Empty)
            {
                empty = true;
                continue;
            }

            open |= reader.EndsOpen;
            if (members is null)
            {
                members = [.. reader.Members];
                continue;
            }

            if (!members.Select(m => (m.Name, m.Presence)).SequenceEqual(reader.Members.Select(m => (m.Name, m.Presence))))
            {
                unknown = Unknown("an item writes different keys on different ways through it", at);
                return null;
            }

            members = [.. members.Zip(reader.Members, (m, n) => m with { Shape = JsonShapes.Join(m.Shape, n.Shape), Nulls = NullReasons.Join(m.Nulls, n.Nulls) })];
        }

        return new MembersText([.. members ?? []], empty, open);
    }

    /// <summary>
    /// What one item of <c>string_agg</c> is inside quotes: the string it is read back as, or an unknown saying why it may hold
    /// a character that would end or break the string.
    /// </summary>
    private static Shape? StringItem(EquatableArray<TextPiece> pieces, SourceLocation at, out UnknownShape? unknown)
    {
        unknown = null;
        if (Expand(pieces) is not { } alternatives)
        {
            unknown = Unknown("an item has too many alternatives to read", at);
            return null;
        }

        Shape? item = null;
        foreach (var sequence in alternatives.Select(Merged))
        {
            var parts = new List<Shape>();
            var literal = "";
            foreach (var piece in sequence)
            {
                switch (piece)
                {
                    case LiteralPiece { Text: var text } when !text.Any(c => c is '"' or '\\' or < ' '):
                        literal += text;
                        parts.Add(LiteralString(text, piece is LiteralPiece { National: true }, ((LiteralPiece)piece).At));
                        break;
                    case SafeTextPiece safe:
                        parts.Add(safe.String);
                        break;
                    case NumberPiece number:
                        parts.Add(number.Text);
                        break;
                    case OpaquePiece { CutShort: true } cut:
                        unknown = CutBefore(cut);
                        return null;
                    case OpaquePiece opaque:
                        unknown = Unquoted(opaque);
                        return null;
                    default:
                        unknown = Unknown("an item in quotes is not text this reads as one string", at);
                        return null;
                }
            }

            var shape = parts.Count == 1 ? parts[0] : sequence.All(p => p is LiteralPiece) ? LiteralString(literal, sequence.All(p => p is LiteralPiece { National: true }), at) : AnyString;
            item = item is null ? shape : JsonShapes.Join(item, shape);
        }

        return item;
    }

    /// <summary>
    /// Each sequence of pieces the text can be, its alternatives taken and the pieces of cut text read as they are (a cut is never
    /// JSON, <see cref="CutPiece"/>), before adjacent literals are joined (<see cref="Merged"/>); null when there are more than
    /// <see cref="MostAlternatives"/>.
    /// </summary>
    private static List<List<TextPiece>>? Expand(EquatableArray<TextPiece> pieces)
    {
        List<List<TextPiece>> sequences = [[]];
        foreach (var piece in pieces)
        {
            List<List<TextPiece>> next = [];
            List<List<TextPiece>> tails = [];
            foreach (var expanded in piece switch
            {
                EitherPiece either => either.Alternatives.Select(Expand),
                CutPiece cut => [Expand(cut.Pieces)],
                _ => [[[piece]]],
            })
            {
                if (expanded is null)
                    return null;
                tails.AddRange(expanded);
            }

            foreach (var head in sequences)
            {
                foreach (var tail in tails)
                    next.Add([.. head, .. tail]);
            }

            if (next.Count > MostAlternatives)
                return null;
            sequences = next;
        }

        return sequences;
    }

    /// <summary>A sequence with each run of adjacent literals joined into one, which is how the server sees their text.</summary>
    private static List<TextPiece> Merged(List<TextPiece> sequence)
    {
        var merged = new List<TextPiece>();
        foreach (var piece in sequence)
        {
            if (piece is LiteralPiece next && merged.Count > 0 && merged[^1] is LiteralPiece last)
                merged[^1] = new LiteralPiece(last.Text + next.Text, last.National && next.National, last.At);
            else if (piece is not LiteralPiece { Text.Length: 0 })
                merged.Add(piece);
        }

        return merged;
    }

    /// <summary>A string, with no facet: what a string written from several pieces is read back as.</summary>
    private static ScalarShape AnyString { get; } = new(new IrScalar(IrScalarKind.String, null, null), null);

    /// <summary>The string a literal's text is read back as inside quotes, with its value; a string with no facet when not read.</summary>
    private static Shape LiteralString(string text, bool national, SourceLocation at) =>
        JsonLiterals.Parse($"[\"{text}\"]", national, at) is TupleShape { Items: [var only] } ? only.Type : AnyString;

    private static UnknownShape Unknown(string why, SourceLocation at) =>
        new(IrUnknownReason.JsonShapeUnknown, $"JSON assembled as text, where {why}, so its shape is not known.", at);

    /// <summary>Text that may be cut short with more after it: what follows comes after a cut that may fall anywhere.</summary>
    private static UnknownShape CutBefore(OpaquePiece cut) =>
        Unknown($"{cut.What} is followed by more text, which would come after a cut that may fall anywhere", cut.At);

    private static UnknownShape Unquoted(OpaquePiece opaque) => Unknown(
        $"{opaque.What} is written inside quotes without string_escape(…, 'json'), so a value holding a quotation mark, a backslash or a control character would change the JSON",
        opaque.At);

    private static bool IsSpace(char c) => c is ' ' or '\t' or '\n' or '\r';
}
