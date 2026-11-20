using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Inference.Lattice;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// Which JSON a string assembled as text holds: <c>'[' + string_agg(…) + ']'</c>, <c>concat('[', a, ',', b, ']')</c>,
/// <c>'[' + concat_ws(',', …) + ']'</c>, read from its <see cref="TextPiece"/>s where the text is read as JSON. It claims only
/// what the server guarantees: each element is a value whose text is exactly one JSON value (a literal, a number converted to
/// text, live JSON) or a string in quotes whose text can't hold a quotation mark, a backslash or a control character; anything
/// else in the brackets makes the JSON unknown, saying why (docs/decisions/0084-json-assembled-as-text-is-typed-where-each-element-is-one-json-value.md).
/// The README in this folder, "JSON assembled as text", gives the rules and the observations they rest on.
/// </summary>
/// <remarks>
/// <para>
/// <b>Alternatives.</b> Text that is one of several (<see cref="EitherPiece"/>) is read as each, and the JSON is the join of what
/// each holds (<see cref="JsonShapes.Join"/>). Text that is not JSON, such as <c>[,{"a":1}]</c> left by a NULL in
/// <c>concat</c>, is an error where it is read (13609, observed:
/// A_null_fragment_in_concat_leaves_a_stray_comma_which_json_query_refuses_unless_it_is_the_only_one), so it describes no call
/// that succeeds and adds nothing.
/// </para>
/// <para>
/// <b>Only the first value is read.</b> <c>json_query</c> reads text as far as its first value and ignores the rest, so a
/// value in quotes that holds <c>"]</c> would end the array early and the read would succeed (observed:
/// An_unescaped_value_in_quotes_can_make_more_elements_a_number_or_cut_the_array_short). That is why a value in quotes must be
/// escaped, and why text after the array is not read here: it is unknown, which claims less.
/// </para>
/// </remarks>
internal static class JsonAssembly
{
    /// <summary>The most alternatives read; text with more is not read (it claims nothing).</summary>
    private const int MostAlternatives = 64;

    /// <summary>
    /// The JSON the text holds when it is JSON assembled here: its shape, or an unknown saying why it is not known. Null when the
    /// text is not an array assembled from pieces (a plain string, a literal on its own), or when every way it can be is not
    /// JSON, which claims nothing.
    /// </summary>
    /// <param name="pieces">The text's pieces.</param>
    /// <param name="at">Where it is read, for an unknown with no piece of its own to name.</param>
    public static Shape? Parse(EquatableArray<TextPiece> pieces, SourceLocation at)
    {
        if (Expand(pieces) is not { } alternatives)
            return null;
        Shape? content = null;
        UnknownShape? unknown = null;
        var assembled = false;
        foreach (var sequence in alternatives)
        {
            var reader = new Reader(sequence, at, single: false);
            var read = reader.Read();
            assembled |= reader.Assembled;
            if (read is UnknownShape why)
                unknown ??= why;
            else if (read is not null)
                content = content is null ? read : JsonShapes.Join(content, read);
        }

        return !assembled ? null : unknown ?? content;
    }

    /// <summary>
    /// What one item of <c>string_agg</c> or <c>concat_ws</c> is when its text is exactly one JSON value: its shape and why it may be
    /// JSON null, or an unknown saying why not. Null when no way it can be is JSON.
    /// </summary>
    private static (Shape Shape, EquatableArray<IrNullReason> Nulls)? Item(EquatableArray<TextPiece> pieces, SourceLocation at, out UnknownShape? unknown)
    {
        unknown = null;
        if (Expand(pieces) is not { } alternatives)
        {
            unknown = Unknown("an item has too many alternatives to read", at);
            return null;
        }

        (Shape Shape, EquatableArray<IrNullReason> Nulls)? item = null;
        foreach (var sequence in alternatives)
        {
            var reader = new Reader(sequence, at, single: true);
            var read = reader.Read();
            if (read is UnknownShape why)
            {
                unknown = why;
                return null;
            }

            if (read is null)
                continue;
            var one = reader.Element;
            item = item is not { } some ? one : (JsonShapes.Join(some.Shape, one.Shape), NullReasons.Join(some.Nulls, one.Nulls));
        }

        return item;
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
        foreach (var sequence in alternatives)
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
    /// JSON, <see cref="CutPiece"/>), adjacent literals joined; null when there are more than <see cref="MostAlternatives"/>.
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

        return [.. sequences.Select(Merged)];
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

    private static UnknownShape Unquoted(OpaquePiece opaque) => Unknown(
        $"{opaque.What} is written inside quotes without string_escape(…, 'json'), so a value holding a quotation mark, a backslash or a control character would change the array",
        opaque.At);

    private static bool IsSpace(char c) => c is ' ' or '\t' or '\n' or '\r';

    /// <summary>
    /// Reads one sequence of pieces, left to right, as a JSON array (or, for an item, one JSON value), as a parser reads text:
    /// literals character by character, and every other piece as the text it stands for.
    /// </summary>
    private sealed class Reader(List<TextPiece> sequence, SourceLocation at, bool single)
    {
        private enum State
        {
            /// <summary>Before anything: the array's <c>[</c> (or, for an item, its value).</summary>
            Start,

            /// <summary>After <c>[</c>: a value or <c>]</c>.</summary>
            Open,

            /// <summary>After <c>,</c>: a value.</summary>
            Comma,

            /// <summary>After a value: <c>,</c> or <c>]</c>.</summary>
            After,

            /// <summary>After <c>concat_ws</c> that may have written nothing: <c>]</c> only.</summary>
            Gap,

            /// <summary>Inside a string's quotes.</summary>
            InString,

            /// <summary>After the array's <c>]</c>.</summary>
            Done,
        }

        private readonly List<(Shape Shape, EquatableArray<IrNullReason> Nulls, int Min, int? Max)> groups = [];
        private readonly List<Shape> parts = [];
        private State state = single ? State.Comma : State.Start;
        private Shape? whole;
        private UnknownShape? unknown;
        private bool notJson;
        private string literalInString = "";
        private bool literalOnly = true;
        private Shape? listItem;
        private bool afterNumber;
        private bool endsInToken;

        /// <summary>Whether the text opens an array across pieces, or is one JSON value assembled here: whether it is this idiom.</summary>
        public bool Assembled { get; private set; }

        /// <summary>For an item, the one value read.</summary>
        public (Shape Shape, EquatableArray<IrNullReason> Nulls) Element => (groups[0].Shape, groups[0].Nulls);

        /// <summary>The JSON read, an unknown saying why it is not known, or null when this text is never JSON.</summary>
        public Shape? Read()
        {
            for (var i = 0; i < sequence.Count; i++)
            {
                var piece = sequence[i];
                var going = endsInToken && piece is not LiteralPiece
                    ? Stop("a number or word is written partly in a literal and partly by a value", at)
                    : piece switch
                    {
                        LiteralPiece literal => Literal(literal, i == 0),
                        NumberPiece number => Number(number),
                        SafeTextPiece safe => state == State.InString ? Part(safe.String, safe.At) : Stop(safe.Unquoted, safe.At),
                        JsonPiece json => Json(json.Content, i == 0),
                        ListPiece list => List(list),
                        JoinedPiece joined => Joined(joined),
                        OpaquePiece opaque => Stop(state == State.InString ? Unquoted(opaque) : Unknown($"{opaque.What} is written into the brackets as text, which may not be one JSON value", opaque.At)),
                        _ => Stop("text this does not read is written into the brackets", at),
                    };
                if (!going)
                    return notJson ? null : unknown;
            }

            if (single)
            {
                if (state == State.After && groups is [{ Min: 1, Max: 1 } only])
                    return only.Shape;
                return state is State.Comma or State.InString ? null : Unknown("an item may be more than one JSON value", at);
            }

            if (state == State.Start)
                return Unknown("one way the text can be is empty", at);
            if (state != State.Done)
                return null;
            if (whole is not null)
                return whole;
            if (groups.All(g => g is { Min: 1, Max: 1 }))
                return new TupleShape([.. groups.Select(g => new ElementShape(g.Shape, g.Nulls))]);
            var min = groups.Sum(g => g.Min);
            int? max = groups.Any(g => g.Max is null) ? null : groups.Sum(g => g.Max ?? 0);
            return new ArrayShape(
                groups.Select(g => g.Shape).Aggregate(JsonShapes.Join),
                groups.Select(g => g.Nulls).Aggregate((EquatableArray<IrNullReason>)[], NullReasons.Join),
                min > 0 ? min : null,
                max);
        }

        /// <summary>Stops reading: the JSON is not known, for this reason.</summary>
        private bool Stop(string why, SourceLocation where) => Stop(Unknown(why, where));

        private bool Stop(UnknownShape why)
        {
            unknown = why;
            return false;
        }

        /// <summary>Stops reading: this text is not JSON, so reading it is an error, and it describes no call that succeeds.</summary>
        private bool NotJson()
        {
            notJson = true;
            return false;
        }

        private bool Value(Shape shape, EquatableArray<IrNullReason> nulls)
        {
            groups.Add((shape, nulls, 1, 1));
            state = State.After;
            return true;
        }

        private bool Part(Shape shape, SourceLocation where)
        {
            if (listItem is not null)
                return Stop("text is written beside a string_agg inside the same quotes", where);
            parts.Add(shape);
            literalOnly = false;
            return true;
        }

        private bool Literal(LiteralPiece literal, bool first)
        {
            var text = literal.Text;
            if (afterNumber && text.Length > 0 && (char.IsAsciiDigit(text[0]) || text[0] is '.' or 'e' or 'E'))
                return Stop("a value converted to a number is followed by a literal that continues it", literal.At);
            afterNumber = false;
            var i = 0;
            while (i < text.Length)
            {
                var c = text[i];
                if (state == State.InString)
                {
                    var close = text.IndexOf('"', i);
                    var inner = close < 0 ? text[i..] : text[i..close];
                    if (inner.Any(ch => ch is '\\' or < ' '))
                        return Stop("a literal inside quotes holds an escape or a control character", literal.At);
                    if (inner.Length > 0)
                    {
                        if (listItem is not null)
                            return Stop("text is written beside a string_agg inside the same quotes", literal.At);
                        literalInString += inner;
                        parts.Add(LiteralString(inner, literal.National, literal.At));
                    }

                    if (close < 0)
                        return true;
                    CloseString(literal);
                    i = close + 1;
                    continue;
                }

                if (IsSpace(c))
                {
                    i++;
                    continue;
                }

                switch (state)
                {
                    case State.Start:
                        // Not an array or object: an error for json_query, but text such as '' or '1,2' put in brackets later
                        // would be JSON of another shape, so it is not dropped as one that describes no call.
                        if (c is not ('[' or '{'))
                            return Stop("one way the text can be is not an array", literal.At);
                        if (End(text, i) is { } end)
                        {
                            if (!Parsed(text[i..end], literal, out var parsed))
                                return false;
                            whole = parsed;
                            state = State.Done;
                            i = end;
                            continue;
                        }

                        if (c == '{')
                            return Stop("an object is written across several pieces, which is not read yet", literal.At);
                        Assembled = first;
                        state = State.Open;
                        i++;
                        continue;
                    case State.Open or State.Gap when c == ']':
                        state = State.Done;
                        i++;
                        continue;
                    case State.Gap:
                        return Stop("concat_ws, which may write nothing, is followed by more than the closing bracket", literal.At);
                    case State.Open or State.Comma when c == '"' && text.IndexOf('"', i + 1) < 0:
                    {
                        // A string the literal opens and a later piece closes.
                        var inner = text[(i + 1)..];
                        if (inner.Any(ch => ch is '\\' or < ' '))
                            return Stop("a literal inside quotes holds an escape or a control character", literal.At);
                        state = State.InString;
                        literalInString = inner;
                        literalOnly = true;
                        parts.Clear();
                        if (inner.Length > 0)
                            parts.Add(LiteralString(inner, literal.National, literal.At));
                        return true;
                    }

                    case State.Open or State.Comma:
                    {
                        var stop = End(text, i);
                        if (stop is null && c is '[' or '{')
                            return Stop("an array or object inside it is written across several pieces, which is not read yet", literal.At);
                        endsInToken = stop is null;
                        var token = text[i..(stop ?? text.Length)];
                        if (!Parsed(token, literal, out var element))
                            return false;
                        Value(element, token == "null" ? [new IrNullReason(IrNullRule.NullLiteral, literal.At.ToString())] : []);
                        i += token.Length;
                        continue;
                    }

                    case State.After when c == ',' && !single:
                        state = State.Comma;
                        i++;
                        continue;
                    case State.After when c == ']' && !single:
                        state = State.Done;
                        i++;
                        continue;
                    case State.Done:
                        return Stop("text follows the array, which json_query would ignore", literal.At);
                    default:
                        return single ? Stop("an item may be more than one JSON value", literal.At) : NotJson();
                }
            }

            return true;
        }

        /// <summary>
        /// Where the JSON value starting at <paramref name="start"/> ends, when it ends within the text: a string after its
        /// closing quote, an array or object after its closing bracket, anything else before a comma, a bracket or a space. Null
        /// when the text ends first.
        /// </summary>
        private static int? End(string text, int start)
        {
            var c = text[start];
            if (c == '"')
            {
                for (var i = start + 1; i < text.Length; i++)
                {
                    if (text[i] == '\\')
                        i++;
                    else if (text[i] == '"')
                        return i + 1;
                }

                return null;
            }

            if (c is '[' or '{')
            {
                var depth = 0;
                var inString = false;
                for (var i = start; i < text.Length; i++)
                {
                    var d = text[i];
                    if (inString)
                    {
                        if (d == '\\')
                            i++;
                        else if (d == '"')
                            inString = false;
                    }
                    else if (d == '"')
                    {
                        inString = true;
                    }
                    else if (d is '[' or '{')
                    {
                        depth++;
                    }
                    else if (d is ']' or '}' && --depth == 0)
                    {
                        return i + 1;
                    }
                }

                return null;
            }

            for (var i = start; i < text.Length; i++)
            {
                if (text[i] is ',' or ']' or '}' || IsSpace(text[i]))
                    return i;
            }

            return null;
        }

        /// <summary>A whole JSON value written in a literal: its shape; false, having said why, when it is not read or not JSON.</summary>
        private bool Parsed(string value, LiteralPiece literal, out Shape shape)
        {
            shape = AnyString;
            var wrapped = $"[{value}]";
            if (!JsonLiterals.IsDocumentText(wrapped))
                return NotJson();
            switch (JsonLiterals.Parse(wrapped, literal.National, literal.At))
            {
                case TupleShape { Items: [var only] }:
                    shape = only.Type;
                    return true;
                case UnknownShape why:
                    return Stop(why);
                default:
                    return NotJson();
            }
        }

        private void CloseString(LiteralPiece closing)
        {
            var shape = listItem ?? (parts.Count == 1 ? parts[0] : literalOnly ? LiteralString(literalInString, closing.National, closing.At) : AnyString);
            groups.Add((shape, [], 1, listItem is null ? 1 : null));
            state = State.After;
            listItem = null;
            literalInString = "";
            literalOnly = true;
            parts.Clear();
        }

        private bool Number(NumberPiece number)
        {
            switch (state)
            {
                case State.InString:
                    return Part(number.Text, at);
                case State.Open or State.Comma:
                    afterNumber = true;
                    return Value(number.Number, []);
                case State.After:
                    return NotJson();
                default:
                    return Stop("a number is written outside the array", at);
            }
        }

        private bool Json(Shape content, bool first)
        {
            switch (state)
            {
                case State.Start when first && sequence.Count == 1:
                    Assembled = true;
                    whole = content;
                    state = State.Done;
                    return true;
                case State.Open or State.Comma:
                    return Value(content, []);
                case State.InString:
                    return Stop("JSON is written inside quotes, where it is not escaped", at);
                case State.After:
                    return NotJson();
                default:
                    return Stop("JSON is written outside the array", at);
            }
        }

        private bool List(ListPiece list)
        {
            if (state == State.InString)
            {
                // ["…" + string_agg(x, '","') + "…"]: each item a string, the separator closing one and opening the next.
                if (parts.Count > 0 || listItem is not null || !IsSeparator(list.Separator, quoted: true))
                    return Stop("string_agg inside quotes is not the only text there, or its separator is not \",\"", at);
                if (StringItem(list.Item, at, out var why) is not { } item)
                    return why is null ? NotJson() : Stop(why);
                listItem = item;
                literalOnly = false;
                return true;
            }

            if (state is not (State.Open or State.Comma))
                return state == State.After ? NotJson() : Stop("string_agg is written outside the array", at);
            if (!IsSeparator(list.Separator, quoted: false))
                return Stop("string_agg's separator is not a literal comma", at);
            if (Item(list.Item, at, out var unknownItem) is not { } element)
                return unknownItem is null ? NotJson() : Stop(unknownItem);
            groups.Add((element.Shape, element.Nulls, 1, null));
            state = State.After;
            return true;
        }

        private bool Joined(JoinedPiece joined)
        {
            if (state is not (State.Open or State.Comma) || !IsSeparator(joined.Separator, quoted: false))
                return Stop("concat_ws is not the whole of the array between its brackets, or its separator is not a literal comma", at);
            var shapes = new List<(Shape Shape, EquatableArray<IrNullReason> Nulls)>();
            foreach (var item in joined.Items)
            {
                if (Item(item.Pieces, at, out var why) is { } element)
                    shapes.Add(element);
                else if (why is not null)
                    return Stop(why);
                else if (!item.MayBeAbsent)
                    return NotJson();
            }

            var min = joined.Items.Count(i => !i.MayBeAbsent);
            if (shapes.Count > 0)
                groups.Add((shapes.Select(s => s.Shape).Aggregate(JsonShapes.Join), shapes.Select(s => s.Nulls).Aggregate(NullReasons.Join), min, shapes.Count));
            if (min > 0)
            {
                state = State.After;
                return true;
            }

            if (state == State.Comma)
                return Stop("concat_ws, which may write nothing, follows a comma", at);
            state = State.Gap;
            return true;
        }

        /// <summary>Whether a separator is a literal comma, with spaces around it, or, inside quotes, <c>","</c>.</summary>
        private static bool IsSeparator(EquatableArray<TextPiece> separator, bool quoted) =>
            separator is [LiteralPiece { Text: var text }]
            && (quoted
                ? text.Length >= 3 && text[0] == '"' && text[^1] == '"' && text[1..^1].Trim(' ', '\t', '\n', '\r') == ","
                : text.Trim(' ', '\t', '\n', '\r') == ",");
    }
}
