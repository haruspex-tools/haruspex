using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Haruspex.Emit.JsonSchema.Model;
using Haruspex.Ir;

namespace Haruspex.Emit.JsonSchema.Lowering;

/// <summary>
/// A string with every fact the IR carries about it, as JSON Schema keywords: its value set as <c>enum</c> or <c>pattern</c>, and
/// its lengths as <c>minLength</c> and <c>maxLength</c>. A fact JSON Schema can't hold exactly is widened, and a note says how.
/// </summary>
/// <remarks>
/// <para><b>Lengths.</b> JSON Schema counts a string's length in code points; SQL Server counts UTF-16 code units
/// (<c>nvarchar</c>) or bytes (<c>varchar</c>). A code point is one or two UTF-16 code units, and one to four bytes in UTF-8 (two in
/// a double-byte collation, one in a single-byte one), so a maximum of <c>n</c> of either is at most <c>n</c> code points, always
/// true but looser where a character takes more than one; a fixed length of <c>n</c> is at least <c>n</c> divided by the most a
/// character takes, rounded up. Only a single-byte collation's lengths are exact.</para>
/// <para><b>Value sets.</b> An exact set is <c>enum</c>. A set that holds as the database compares strings (<c>collation</c>) or
/// up to a fold (<c>canonical</c>) is not a set of exact strings, and JSON Schema has no keyword for a fold, so it is a note on a
/// plain string. A <c>padded</c> set is an item followed by spaces, which <c>pattern</c> says exactly.</para>
/// </remarks>
internal static class Strings
{
    /// <summary>The string's schema, with a note for each fact widened.</summary>
    public static Schema Of(IrScalar scalar)
    {
        var facets = scalar.Facets;
        if (facets?.Values is { Match: IrValueMatch.Exact } exact && exact.Items.All(i => i.IsString))
        {
            return exact.Items.Count == 0
                ? Schema.Never
                : Schema.Any.With(Keyword.Enum, new SchemaValue.List([.. exact.Items.Select(i => new SchemaValue.Text(i.Text))]));
        }

        var schema = Schema.OfType("string");
        var notes = new List<SchemaNote>();
        if (facets?.Values is { Match: IrValueMatch.Collation or IrValueMatch.Canonical } compared)
        {
            var fold = compared.Fold is { } name ? $" (canonical under {name}, a fold JSON Schema can't apply)" : "";
            notes.Add(new SchemaNote($"One of {string.Join(", ", compared.Items.Select(i => Quoted(i.Text)))}, as the database compares strings{fold}.", JsonSchemaCaveat.CollationValueSets));
        }

        if (facets?.Values is { Match: IrValueMatch.Padded } padded && padded.Items.All(i => i.IsString))
        {
            // Trimmed again, although the IR stores them trimmed: an item's own trailing spaces would make the pattern require
            // them, and a value without them is in the set ('a' = 'a ').
            var items = padded.Items.Select(i => Escaped(i.Text.TrimEnd(' '))).Distinct(StringComparer.Ordinal);
            schema = schema.With(Keyword.Pattern, new SchemaValue.Text($"^(?:{string.Join("|", items)}) *$"));
        }

        int? fewest = null;
        int? most = null;
        if (facets?.Length is { } length)
        {
            most = length.Value;
            if (length.IsFixed)
                fewest = Ceiling(length.Value, UnitsPerCodePoint(length));
            if (Widened(scalar.Sql ?? "The string", length, fewest) is { } note)
                notes.Add(note);
        }

        if (facets?.MinLength is { } minLength)
        {
            var points = Ceiling(minLength.Value, 2);
            fewest = Math.Max(fewest ?? 0, points);
            if (minLength.Value > 1)
                notes.Add(new SchemaNote($"At least {minLength.Value} UTF-16 code units, which is at least {points} code points.", JsonSchemaCaveat.CodePoints));
        }

        return schema.With(Keyword.MinLength, fewest > 0 ? fewest : null).With(Keyword.MaxLength, most).Noted(notes);
    }

    /// <summary>The most units of <paramref name="length"/> one code point takes.</summary>
    private static int UnitsPerCodePoint(IrLength length) => (length.Unit, length.Encoding) switch
    {
        (IrLengthUnit.Utf16, _) => 2,
        (IrLengthUnit.Bytes, IrByteEncoding.SingleByte) => 1,
        (IrLengthUnit.Bytes, IrByteEncoding.DoubleByte) => 2,

        // UTF-8, or an encoding not known: no character set SQL Server stores takes more than four bytes a character.
        (IrLengthUnit.Bytes, _) => 4,
    };

    /// <summary>How a length was widened into code points, when it was; null for a single-byte collation's, which is exact.</summary>
    private static SchemaNote? Widened(string sql, IrLength length, int? fewest)
    {
        if (length.Unit == IrLengthUnit.Bytes && length.Encoding == IrByteEncoding.SingleByte)
            return null;
        if (length.Unit == IrLengthUnit.Utf16)
        {
            return length.IsFixed
                ? new SchemaNote($"{sql} is exactly {length.Value} UTF-16 code units, which is {fewest} to {length.Value} code points.", JsonSchemaCaveat.CodePoints)
                : null;
        }

        var counts = length.IsFixed ? $"{sql} pads to {length.Value} bytes" : $"{sql} counts bytes";
        var why = length.Encoding switch
        {
            IrByteEncoding.Utf8 => "UTF-8 takes up to four a character",
            IrByteEncoding.DoubleByte => "its double-byte collation takes up to two a character",
            _ => "its collation isn't declared",
        };
        var range = length.IsFixed ? $"{fewest} to {length.Value}" : $"at most {length.Value}";
        return new SchemaNote($"{counts} and {why}, so the length is {range} code points.", JsonSchemaCaveat.ByteLengths);
    }

    private static int Ceiling(int units, int perCodePoint) => (units + perCodePoint - 1) / perCodePoint;

    /// <summary>
    /// Text as a literal of an ECMA-262 regular expression with the <c>u</c> flag, as JSON Schema's <c>pattern</c> is: each
    /// syntax character escaped and nothing else, since under <c>u</c> escaping any other character is an error.
    /// </summary>
    private static string Escaped(string text)
    {
        var escaped = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if ("^$\\.*+?()[]{}|/".Contains(c, StringComparison.Ordinal))
                escaped.Append('\\');
            escaped.Append(c);
        }

        return escaped.ToString();
    }

    /// <summary>A value quoted inside a note, as it would be written in JSON, so that spaces and quotes in it stay visible.</summary>
    private static string Quoted(string text) => "\"" + JsonEncodedText.Encode(text, JavaScriptEncoder.UnsafeRelaxedJsonEscaping) + "\"";
}
