using Haruspex.Collections;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// One text a value may be when it is sent somewhere, such as the URL a call of a REST endpoint sends: literal text, and numbers
/// converted to text, whose digits are not known but which never hold anything but digits, a sign and a decimal point. What
/// <see cref="Expand"/> makes of a value's pieces (<see cref="Value.Pieces"/>), one text for each way the pieces may go.
/// </summary>
/// <param name="Parts">Its parts in order, two literals never side by side.</param>
internal sealed record SentText(EquatableArray<SentText.Part> Parts)
{
    /// <summary>The most texts a value is expanded into: more, and it is read as text not known.</summary>
    public const int MostTexts = 64;

    /// <summary>
    /// The most characters a number converted to text is written in: 38 digits, a sign and a decimal point. A number written
    /// longer than its target holds is an error or <c>*</c>, which the conversion's own rule reads, not a longer text.
    /// </summary>
    public const int MostNumberCharacters = 40;

    /// <summary>A part of a text.</summary>
    internal abstract record Part;

    /// <summary>Text the SQL writes.</summary>
    internal sealed record Literal(string Text) : Part;

    /// <summary>
    /// A number converted to text (<see cref="NumberPiece"/>): never empty, and never holding a character but a digit, a minus sign
    /// or a decimal point.
    /// </summary>
    /// <param name="MostLength">The most characters it is written in: the string type it is converted to, or <see cref="MostNumberCharacters"/>.</param>
    internal sealed record Number(int MostLength) : Part;

    /// <summary>The whole text when it is all literal; null when a part is a number.</summary>
    public string? Text => Parts.Count == 0 ? "" : Parts is [Literal only] ? only.Text : null;

    /// <summary>The most characters the text may have.</summary>
    public int MostLength => Parts.Sum(p => p switch
    {
        Literal literal => literal.Text.Length,
        Number number => number.MostLength,
        _ => MostNumberCharacters,
    });

    /// <summary>
    /// Every text <paramref name="pieces"/> may be: a text for each choice of a <c>CASE</c>, a <c>COALESCE</c> or a NULL that counts
    /// as empty. Null when a piece may be anything (a parameter not passed known text, a column), may be cut short, is built in a
    /// way not read here, or the choices make more than <see cref="MostTexts"/> texts; <paramref name="why"/> then says which.
    /// </summary>
    public static IReadOnlyList<SentText>? Expand(EquatableArray<TextPiece> pieces, out string why)
    {
        List<List<Part>> texts = [[]];
        foreach (var piece in pieces)
        {
            switch (piece)
            {
                case LiteralPiece { National: false } literal when literal.Text.Any(c => c > '\u007f'):
                    why = $"the literal at {literal.At} is converted to the database's code page, which may change a character that is not ASCII";
                    return null;
                case LiteralPiece literal:
                    texts.ForEach(t => t.Add(new Literal(literal.Text)));
                    break;
                case NumberPiece { Text: var text }:
                    var most = text is ScalarShape { BuiltIn: { IsString: true, IsMax: false } type } ? Math.Min(type.Length, MostNumberCharacters) : MostNumberCharacters;
                    texts.ForEach(t => t.Add(new Number(most)));
                    break;
                case EitherPiece either:
                    var next = new List<List<Part>>();
                    foreach (var alternative in either.Alternatives)
                    {
                        if (Expand(alternative, out why) is not { } ways)
                            return null;
                        next.AddRange(texts.SelectMany(t => ways.Select(w => (List<Part>)[.. t, .. w.Parts])));
                        if (next.Count > MostTexts)
                        {
                            why = $"it may be more than {MostTexts} different texts";
                            return null;
                        }
                    }

                    texts = next;
                    break;
                case OpaquePiece opaque:
                    why = $"{opaque.What} at {opaque.At} may be any text";
                    return null;
                case CutPiece:
                    why = "it may be cut short at the length of its type";
                    return null;
                default:
                    why = "it is text built in a way not read for this";
                    return null;
            }
        }

        why = "";
        return [.. texts.Select(Joined).Distinct()];
    }

    /// <summary>The parts with each run of literals joined into one, so that two texts the same are equal.</summary>
    private static SentText Joined(List<Part> parts)
    {
        var joined = new List<Part>();
        foreach (var part in parts)
        {
            if (part is Literal { Text.Length: 0 })
                continue;
            if (part is Literal next && joined.Count > 0 && joined[^1] is Literal last)
                joined[^1] = new Literal(last.Text + next.Text);
            else
                joined.Add(part);
        }

        return new SentText([.. joined]);
    }
}
