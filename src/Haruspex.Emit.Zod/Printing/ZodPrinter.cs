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
/// The layout is fixed, two spaces of indent and LF line endings, and needs no formatter, so the output is deterministic. It
/// is not the layout a formatter (Prettier, Biome) would write: one would rewrite the file, and <c>haruspex check</c>, which
/// compares the text, would then report it (HAR6015), so the documentation tells users to keep the output folder out of
/// their formatter.
/// </remarks>
internal static class ZodPrinter
{
    /// <summary>
    /// JSON text whose content the IR knows: a string that must parse to a value of the content's schema. The type stays
    /// <c>string</c>, since that is what arrives (the consumer parses it); the check only rejects text that does not hold
    /// what the IR claims. The content is a function so that it may name a schema written later, such as a recursive one.
    /// </summary>
    /// <remarks>
    /// The content's schema is built on the first value checked, by which time the module has finished loading, and kept: a
    /// content of more than a name (<c>z.array(RowSchema).min(1)</c>, a union of objects that <c>.extend</c> each other) costs
    /// a hundred times or more to build than to check a value against, so a list of rows would pay it once a row. Observed:
    /// <c>EmitCheckTests.The_json_text_helper_builds_its_content_s_schema_once_however_many_values_it_checks</c>.
    /// </remarks>
    private const string JsonTextHelper = $$"""

        // JSON text: a string that must parse to a value of its content's schema. Its type stays string. The content's schema is
        // built once, when the first value is checked, so it may name a schema declared later in the module.
        const {{ZodModule.JsonTextHelperName}} = (content: () => {{ZodModule.ZodName}}.ZodType): {{ZodModule.ZodName}}.ZodString => {
          let schema: {{ZodModule.ZodName}}.ZodType | undefined
          return {{ZodModule.ZodName}}.string().refine((text) => {
            try {
              schema ??= content()
              return schema.safeParse(JSON.parse(text)).success
            } catch {
              return false
            }
          }, { error: 'Expected JSON text holding a value of its content type' })
        }

        """;

    /// <summary>
    /// A canonical value set's mapping (<see cref="ZodExpression.Canonical"/>), by a fold as the IR README defines its canonical
    /// form: a value becomes the item whose canonical form it has, the ordinally least where two share one; any other is left as it
    /// is, for the enum to refuse. Each UTF-16 code unit is looked at alone, as the IR's fold is.
    /// </summary>
    private const string CanonicalHelper = $$"""

        // A value set's canonical form, by a fold the IR gives for a column's collation: drop the characters it ignores, put each
        // class's name for its characters, keep the rest, then drop trailing spaces. A value maps to the item whose canonical form
        // it has; any other value is left as it is, for the enum to refuse.
        interface Fold { ignored: string; classes: Record<string, string> }
        const canonicalForm = (fold: Fold, value: string): string => {
          let form = ''
          for (let i = 0; i < value.length; i++) {
            const unit = value.charAt(i)
            if (fold.ignored.includes(unit)) continue
            form += Object.entries(fold.classes).find(([, units]) => units.includes(unit))?.[0] ?? unit
          }
          return form.replace(/ +$/, '')
        }
        const canonicalOf = (fold: Fold, items: readonly string[]): ((value: unknown) => unknown) => {
          const byForm = new Map<string, string>()
          for (const item of items) {
            const form = canonicalForm(fold, item)
            if (!byForm.has(form)) byForm.set(form, item)
          }
          return (value: unknown) => (typeof value === 'string' ? (byForm.get(canonicalForm(fold, value)) ?? value) : value)
        }

        """;

    /// <summary>
    /// The TSDoc tag on a binding exported only for the modules beside it: TypeScript's <c>stripInternal</c> and API tools
    /// leave such a name out of what a package offers.
    /// </summary>
    private const string InternalTag = "@internal";

    /// <summary>The module as text.</summary>
    public static string Print(ZodModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        var output = new StringBuilder();
        foreach (var line in module.Header.SelectMany(Wrapped))
            output.Append(line.Length == 0 ? "//" : "// " + Comment(line)).Append('\n');
        output.Append("import { " + ZodModule.ZodName + " } from 'zod'\n");
        foreach (var import in module.Imports)
            output.Append("import { ").Append(string.Join(", ", import.Names)).Append(" } from ").Append(SingleQuoted(import.From)).Append('\n');
        if (module.JsonTextHelper)
            output.Append(JsonTextHelper);
        if (module.Folds is { Count: > 0 } folds)
        {
            output.Append(CanonicalHelper).Append("const folds = {\n");
            foreach (var (name, fold) in folds.OrderBy(f => f.Key, StringComparer.Ordinal))
            {
                output.Append("  ").Append(DoubleQuoted(name)).Append(": {\n")
                    .Append("    ignored: ").Append(Units(fold.Ignored)).Append(",\n")
                    .Append("    classes: { ")
                    .Append(string.Join(", ", fold.Classes.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => DoubleQuoted(c.Key) + ": " + Units(c.Value))))
                    .Append(" },\n  },\n");
            }

            output.Append("} satisfies Record<string, Fold>\n");
        }

        foreach (var declaration in module.Declarations)
        {
            output.Append('\n');
            // A declaration's own schema may carry notes too (a result that is unknown, or JSON text): they follow its summary.
            output.Append(Documentation(
                [
                    .. declaration.Summary is { } summary ? [summary] : Array.Empty<string>(),
                    .. ZodNotes.Of(declaration.Schema).Select(n => n.Text),
                    .. declaration.Explanation,
                    .. declaration.Internal ? [InternalTag] : Array.Empty<string>(),
                ],
                ""));
            if (!declaration.Exported)
            {
                output.Append(declaration.Internal ? "export const " : "const ").Append(declaration.Name).Append(" = ").Append(Expression(declaration.Schema)).Append('\n');
                continue;
            }

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
            ZodExpression.Number number => JavaScriptNumbers.Literal(number.Value),
            ZodExpression.Text text => DoubleQuoted(text.Value),
            ZodExpression.Boolean boolean => boolean.Value ? "true" : "false",
            ZodExpression.Canonical canonical => ZodModule.ZodName + ".preprocess(canonicalOf(folds[" + DoubleQuoted(canonical.Fold) + "], " + Items(canonical.Items) + "), "
                + ZodModule.ZodName + ".enum(" + Items(canonical.Items) + "))",
            ZodExpression.PaddedValues padded => "/^(?:" + string.Join("|", padded.Values.Select(RegexEscape)) + ")\\u0020*$/",
            ZodExpression.BigIntLiteral big => big.Value + "n",
            ZodExpression.Pattern pattern => "/^" + pattern.Source + "$/",
            ZodExpression.TextRange range => "(value) => " + string.Join(" && ", new[]
            {
                range.Least is { } least ? "value >= " + DoubleQuoted(least) : null,
                range.Greatest is { } greatest ? "value <= " + DoubleQuoted(greatest) : null,
            }.OfType<string>()),
            ZodExpression.Utf8BytesAtMost bytes => "(value) => new TextEncoder().encode(value).length <= " + bytes.Bytes.ToString(CultureInfo.InvariantCulture),
            ZodExpression.UnitsAtMost units => "(value) => value.length <= " + units.Units.ToString(CultureInfo.InvariantCulture),
            ZodExpression.IntegerTextRange integers => "(value) => /^-?\\d+$/.test(value) && BigInt(value) >= " + integers.Least + "n && BigInt(value) <= " + integers.Greatest + "n",
            ZodExpression.PowerOfTen power => power.Exponent == 0 ? "1" : "1e" + power.Exponent.ToString(CultureInfo.InvariantCulture),
            ZodExpression.ArrayLiteral array => "[" + List(array.Items) + "]",
            ZodExpression.ObjectLiteral obj => "{ " + string.Join(", ", obj.Properties.Select(p => Key(p.Key) + ": " + Expression(p.Value))) + " }",
        };
    }

    /// <summary>
    /// An object schema, a property to a line. A property's notes are a documentation comment above it, one line for one
    /// note and a line each for more, so that an editor shows them with the property, and a formatter keeps them with it.
    /// </summary>
    private static string ObjectSchema(ZodExpression.ObjectSchema obj)
    {
        var text = new StringBuilder(ZodModule.ZodName).Append(obj.Strict ? ".strictObject({\n" : obj.Loose ? ".looseObject({\n" : ".object({\n");
        foreach (var property in obj.Properties)
        {
            text.Append(Documentation([.. ZodNotes.Of(property.Value).Select(n => n.Text), .. property.Explanation], "  "));

            if (property.Getter)
                text.Append("  get ").Append(Key(property.Key)).Append("() { return ").Append(Expression(property.Value)).Append(" },\n");
            else
                text.Append("  ").Append(Key(property.Key)).Append(": ").Append(Expression(property.Value)).Append(",\n");
        }

        return text.Append("})").ToString();
    }

    /// <summary>A documentation comment of <paramref name="lines"/>: one line for one, a line each for more, none for none.</summary>
    private static string Documentation(IReadOnlyList<string> lines, string indent) => lines switch
    {
        [] => "",
        [var line] => indent + "/** " + Comment(line) + " */\n",
        _ => indent + "/**\n" + string.Concat(lines.Select(line => indent + " * " + Comment(line) + "\n")) + indent + " */\n",
    };

    /// <summary>
    /// A header line broken between words to fit 120 columns with its <c>// </c>; a list item's continuation lines are
    /// indented under its text.
    /// </summary>
    private static IEnumerable<string> Wrapped(string line)
    {
        const int width = 120 - 3;
        var indent = line.StartsWith("- ", StringComparison.Ordinal) ? "  " : "";
        var current = new StringBuilder();
        foreach (var word in line.Split(' '))
        {
            if (current.Length > 0 && current.Length + 1 + word.Length > width)
            {
                yield return current.ToString();
                current.Clear().Append(indent);
            }

            if (current.Length > 0 && current.ToString() != indent)
                current.Append(' ');
            current.Append(word);
        }

        yield return current.ToString();
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

    private static string Items(IEnumerable<string> items) => "[" + string.Join(", ", items.Select(DoubleQuoted)) + "]";

    /// <summary>
    /// A fold's characters as a string literal, each outside printable ASCII escaped (<c>\u00ad</c>), so that one that prints as
    /// nothing, such as the soft hyphen, can still be read.
    /// </summary>
    private static string Units(string units) =>
        "\"" + string.Concat(units.Select(c => c is >= ' ' and <= '~' and not '"' and not '\\' ? c.ToString() : "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture))) + "\"";

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
