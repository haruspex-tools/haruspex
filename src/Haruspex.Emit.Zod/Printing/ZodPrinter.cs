using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Haruspex.Emit.Zod.Model;
using Haruspex.Ir;

namespace Haruspex.Emit.Zod.Printing;

/// <summary>
/// Writes a <see cref="ZodModule"/> as TypeScript: the only stage of the emitter that knows TypeScript's syntax, its
/// quoting, escaping and layout. It decides nothing about what the module says.
/// </summary>
/// <remarks>
/// The layout is fixed, two spaces of indent and LF line endings, and needs no formatter, so the output is deterministic and
/// a formatter run after it (Prettier, Biome) only restyles it.
/// </remarks>
internal static class ZodPrinter
{
    /// <summary>
    /// JSON text whose content the IR knows: a string that must parse to a value of the content's schema. The type stays
    /// <c>string</c>, since that is what arrives (the consumer parses it); the check only rejects text that does not hold
    /// what the IR claims. The content is a function so that it may name a schema written later, such as a recursive one.
    /// </summary>
    private const string JsonTextHelper = $$"""

        // JSON text: a string that must parse to a value of its content's schema. Its type stays string.
        const {{ZodModule.JsonTextHelperName}} = (content: () => {{ZodModule.ZodName}}.ZodType) =>
          {{ZodModule.ZodName}}.string().refine((text) => {
            try {
              return content().safeParse(JSON.parse(text)).success
            } catch {
              return false
            }
          }, { error: 'Expected JSON text holding a value of its content type' })

        """;

    /// <summary>The module as text.</summary>
    public static string Print(ZodModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        var output = new StringBuilder();
        foreach (var line in module.Header)
            output.Append("// ").Append(Comment(line)).Append('\n');
        output.Append("import { " + ZodModule.ZodName + " } from 'zod'\n");
        foreach (var import in module.Imports)
            output.Append("import { ").Append(string.Join(", ", import.Names)).Append(" } from ").Append(SingleQuoted(import.From)).Append('\n');
        if (module.JsonTextHelper)
            output.Append(JsonTextHelper);

        foreach (var declaration in module.Declarations)
        {
            output.Append('\n');
            if (declaration.Summary is { } summary)
                output.Append("/** ").Append(Comment(summary)).Append(" */\n");
            var schema = ZodModule.SchemaOf(declaration.Name);
            output.Append("export const ").Append(schema).Append(" = ").Append(Expression(declaration.Schema)).Append('\n')
                .Append("export type ").Append(declaration.Name).Append(" = " + ZodModule.ZodName + ".infer<typeof ").Append(schema).Append(">\n");
        }

        return output.ToString();
    }

    /// <summary>An expression as TypeScript, on one line but for an object schema, which puts each property on its own.</summary>
    public static string Expression(ZodExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return expression switch
        {
            ZodExpression.Name name => name.Path,
            ZodExpression.Call call => Expression(call.Target) + "." + call.Method + "(" + List(call.Arguments) + ")",
            ZodExpression.Union union => ZodModule.ZodName + ".union([" + List(union.Variants) + "])",
            ZodExpression.ObjectSchema obj => ObjectSchema(obj),
            ZodExpression.JsonText text => ZodModule.JsonTextHelperName + "(() => " + Expression(text.Content) + ")",
            ZodExpression.Noted noted => Expression(noted.Expression),
            ZodExpression.Number number => number.Value,
            ZodExpression.Text text => DoubleQuoted(text.Value),
            ZodExpression.Boolean boolean => boolean.Value ? "true" : "false",
            ZodExpression.PaddedValues padded => "/^(?:" + string.Join("|", padded.Values.Select(RegexEscape)) + ")\\u0020*$/",
            ZodExpression.PowerOfTen power => power.Exponent == 0 ? "1" : "1e" + power.Exponent.ToString(CultureInfo.InvariantCulture),
            ZodExpression.ArrayLiteral array => "[" + List(array.Items) + "]",
            ZodExpression.ObjectLiteral obj => "{ " + string.Join(", ", obj.Properties.Select(p => Key(p.Key) + ": " + Expression(p.Value))) + " }",
            _ => throw new ArgumentOutOfRangeException(nameof(expression), expression, "An expression outside the model's closed set."),
        };
    }

    /// <summary>
    /// An object schema, a property to a line. A property's notes follow its value as a block comment, so they stay with the
    /// property when a formatter wraps it.
    /// </summary>
    private static string ObjectSchema(ZodExpression.ObjectSchema obj)
    {
        var text = new StringBuilder(ZodModule.ZodName).Append(obj.Strict ? ".strictObject({\n" : ".object({\n");
        foreach (var property in obj.Properties)
        {
            var value = Expression(property.Value) + (ZodNotes.Of(property.Value) is { } notes ? " /* " + Comment(notes) + " */" : "");
            if (property.Getter)
                text.Append("  get ").Append(Key(property.Key)).Append("() { return ").Append(value).Append(" },\n");
            else
                text.Append("  ").Append(Key(property.Key)).Append(": ").Append(value).Append(",\n");
        }

        return text.Append("})").ToString();
    }

    /// <summary>
    /// Text that matches itself in a regular expression literal: anything but a letter, digit or underscore as a
    /// <c>\uXXXX</c> escape, which can't be a metacharacter, close the literal or break the line. Without the
    /// <c>u</c> flag, a character outside the BMP is two such escapes, which match its UTF-16 code units in turn.
    /// </summary>
    private static string RegexEscape(string text) =>
        string.Concat(text.Select(c => char.IsAsciiLetterOrDigit(c) || c == '_' ? c.ToString() : $"\\u{(int)c:x4}"));

    private static string List(IEnumerable<ZodExpression> items) => string.Join(", ", items.Select(Expression));

    /// <summary>A property key: as it is when it is an identifier, otherwise a quoted string. A reserved word is a valid key.</summary>
    private static string Key(string name) => IrIdentifiers.IsIdentifier(name) ? name : DoubleQuoted(name);

    private static string DoubleQuoted(string text) => "\"" + Escaped(text) + "\"";

    private static string SingleQuoted(string text) => "'" + Escaped(text).Replace("'", "\\'", StringComparison.Ordinal) + "'";

    /// <summary>A string's characters escaped as in JSON, which is valid inside a TypeScript string literal of either quote.</summary>
    private static string Escaped(string text) => JsonEncodedText.Encode(text, JavaScriptEncoder.UnsafeRelaxedJsonEscaping).ToString();

    /// <summary>
    /// Text made safe inside a comment. Names come from the IR, which comes from SQL where a bracketed identifier may hold
    /// anything, so a <c>*/</c> or a line break must not end the comment early.
    /// </summary>
    private static string Comment(string text) =>
        text.Replace("*/", "*\\/", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\u2028", " ", StringComparison.Ordinal)
            .Replace("\u2029", " ", StringComparison.Ordinal);
}
