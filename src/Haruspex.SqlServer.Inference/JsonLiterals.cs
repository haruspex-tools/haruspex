using System.Text.Json;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Semantics;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// What a string literal holds as JSON: its exact shape when it is valid JSON whose top level is an object or an
/// array, the only documents <c>json_query</c>, <c>OPENJSON</c> and <c>JSON_VALUE</c> accept (observed:
/// A_json_literal_is_read_exactly_and_a_bare_scalar_or_text_that_is_not_json_is_an_error).
/// </summary>
/// <remarks>
/// <para>
/// The server checks a document only as far as its first value: <c>'[1]x'</c> is read as <c>[1]</c> and <c>'[1] [2]'</c> as
/// <c>[1]</c> (observed). Such a literal is not valid JSON, so it is not modelled: it is unknown, which claims less. Whitespace
/// around the value is allowed, <c>''</c> and <c>N''</c> alike are errors, and so is a bare scalar (<c>'1'</c>,
/// <c>'"a"'</c>, <c>'null'</c>, <c>'true'</c>), in lax mode too.
/// </para>
/// <para>
/// The text is embedded as it is written (<c>[ 1 , "a" ]</c> keeps its spaces, <c>1.50</c> its zero, a duplicated key both
/// times), so a consumer parses what the literal says. An object with a duplicated key is not modelled, since parsers differ
/// on which value wins, and so is a number past a double's range, which a client reads as Infinity. A literal without N is read
/// in the database's code page, so one with a string or key that is not ASCII is not modelled (<see cref="Parse"/>). Strings and
/// numbers have value sets of exactly their values (fact source <c>expression</c>, the
/// literal's location); a number whose value a JavaScript number cannot hold exactly has none. A JSON <c>null</c> is
/// <c>null-literal</c>. An array is a tuple, each position its own type, so <c>'[]'</c> is the empty tuple, which joins with
/// any array (<see cref="ShapeJoin.Content"/>).
/// </para>
/// </remarks>
internal static class JsonLiterals
{
    /// <summary>The literal's shape, or why it has none (an unknown at <paramref name="at"/>).</summary>
    /// <param name="national">
    /// Whether it is an <c>N'…'</c> literal. One without <c>N</c> is converted to the database's code page before it is read, its
    /// escapes decoded first (observed: A_literal_without_n_is_converted_to_the_code_page_before_it_is_read_as_json), so a string
    /// or key that is not ASCII may come out changed (<c>'["ā"]'</c> as <c>["a"]</c>): such a literal is not read. Which
    /// characters the code page has is not known here.
    /// </param>
    public static Shape Parse(string text, bool national, SourceLocation at)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 128 });
        }
        catch (JsonException)
        {
            return NotJson(text, at, "is not valid JSON");
        }

        using (document)
        {
            if (document.RootElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
                return NotJson(text, at, "is a bare JSON scalar, which json_query, OPENJSON and JSON_VALUE refuse (13609)");
            var why = new string?[1];
            return Of(document.RootElement, national, at, why) ?? NotJson(text, at, why[0]!);
        }
    }

    /// <summary>Whether a literal is valid JSON of an object or array (its shape is not an unknown).</summary>
    public static bool IsDocument(Shape parsed) => parsed is not UnknownShape;

    private static UnknownShape NotJson(string text, SourceLocation at, string why)
    {
        var shown = text.Length > 40 ? text[..40] + "…" : text;
        return new UnknownShape(IrUnknownReason.JsonShapeUnknown, $"The literal '{shown}' {why}, so it is not read as JSON here.", at);
    }

    /// <summary>A JSON value's shape; null when it is not read, <paramref name="why"/>[0] saying why.</summary>
    private static Shape? Of(JsonElement element, bool national, SourceLocation at, string?[] why)
    {
        static bool Ascii(string s) => s.All(c => c <= '\u007f');
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var fields = new List<FieldShape>();
                foreach (var property in element.EnumerateObject())
                {
                    if (fields.Any(f => f.Name == property.Name))
                    {
                        why[0] = "has an object with a key written twice, which parsers read differently";
                        return null;
                    }

                    if (!national && !Ascii(property.Name))
                    {
                        why[0] = "is not an N'' literal and has a key that is not ASCII, which the database's code page may change";
                        return null;
                    }

                    if (Of(property.Value, national, at, why) is not { } type)
                        return null;
                    fields.Add(new FieldShape(property.Name, type, IrPresence.Always, Null(property.Value, at)));
                }

                return new ObjectShape([.. fields]);
            case JsonValueKind.Array:
                var items = new List<ElementShape>();
                foreach (var item in element.EnumerateArray())
                {
                    if (Of(item, national, at, why) is not { } type)
                        return null;
                    items.Add(new ElementShape(type, Null(item, at)));
                }

                return new TupleShape([.. items]);
            case JsonValueKind.String:
                var text = element.GetString()!;
                if (!national && !Ascii(text))
                {
                    why[0] = "is not an N'' literal and has a string that is not ASCII, which the database's code page may change";
                    return null;
                }

                return Scalar(IrScalarKind.String, new IrValue(text, IsString: true), at);
            case JsonValueKind.Number:
                var raw = element.GetRawText();

                // A number past a double's range is written as it is, but a client reads it as Infinity, which is not a number
                // a schema accepts.
                if (!double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) || double.IsInfinity(d))
                {
                    why[0] = $"has the number {raw}, which a client reads as Infinity";
                    return null;
                }

                return ExactDecimal.TryParse(Plain(raw), out var number) && Exact(raw)
                    ? Scalar(IrScalarKind.Number, new IrValue(number.ToString(), IsString: false), at)
                    : new ScalarShape(new IrScalar(IrScalarKind.Number, null, null));
            case JsonValueKind.True:
            case JsonValueKind.False:
                return new ScalarShape(new IrScalar(IrScalarKind.Boolean, null, null));
            default:
                // JSON null: the value is null, its type anything; a string is as good as any, and the reason says it is null.
                return new ScalarShape(new IrScalar(IrScalarKind.String, null, null));
        }
    }

    private static EquatableArray<IrNullReason> Null(JsonElement element, SourceLocation at) =>
        element.ValueKind == JsonValueKind.Null ? [new IrNullReason(IrNullRule.NullLiteral, at.ToString())] : [];

    private static ScalarShape Scalar(IrScalarKind kind, IrValue value, SourceLocation at) =>
        new(new IrScalar(kind, null, null, new IrFacets(Values: new IrValueSet([value], IrValueMatch.Exact, [new IrFactSource(IrFactSourceKind.Expression, at.ToString())]))));

    /// <summary>A JSON number written without an exponent, which <see cref="ExactDecimal"/> reads; null when it has one.</summary>
    private static string Plain(string raw) => raw.Contains('e', StringComparison.OrdinalIgnoreCase) ? "" : raw;

    /// <summary>Whether a JavaScript number holds the value exactly: an integer within 2^53, or a decimal that round-trips.</summary>
    private static bool Exact(string raw)
    {
        if (!double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) || double.IsInfinity(d))
            return false;
        return ExactDecimal.TryParse(Plain(raw), out var parsed)
            && ExactDecimal.TryParse(d.ToString("R", System.Globalization.CultureInfo.InvariantCulture), out var back)
            && parsed.CompareTo(back) == 0;
    }
}
