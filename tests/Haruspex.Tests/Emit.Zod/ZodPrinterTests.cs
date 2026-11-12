using Haruspex.Emit.Zod.Model;
using Haruspex.Emit.Zod.Printing;
using static Haruspex.Emit.Zod.Model.ZodExpression;

namespace Haruspex.Tests.Emit.Zod;

/// <summary>Each expression form, and a module, written as TypeScript.</summary>
public class ZodPrinterTests
{
    /// <summary>Each form by name, with what it prints as. Keyed by name because the model is internal to the emitter.</summary>
    private static readonly Dictionary<string, (ZodExpression Expression, string Printed)> Forms = new()
    {
        ["a name"] = (new Name("OrderSchema"), "OrderSchema"),
        ["a call chain"] = (Z("int").Then("min", new Number("0")).Then("max", new Number("99.99")), "z.int().min(0).max(99.99)"),
        ["a path and an object argument"] = (new Call(new Name("z.iso"), "datetime", [new ObjectLiteral([new ZodProperty("offset", new ZodExpression.Boolean(true))])]), "z.iso.datetime({ offset: true })"),
        ["a union"] = (new Union([Z("string"), Z("number")]), "z.union([z.string(), z.number()])"),
        ["json text"] = (new JsonText(new Name("PayloadSchema")), "jsonText(() => PayloadSchema)"),
        ["strings, escaped"] = (Z("enum", new ArrayLiteral([new Text("a \"b\""), new Text("c\nd")])), "z.enum([\"a \\\"b\\\"\", \"c\\nd\"])"),
        ["numbers"] = (Z("literal", new ArrayLiteral([new Number("1"), new Number("-2")])), "z.literal([1, -2])"),
        ["a regular expression"] = (Z("string").Then("regex", new Regex("^(?:a)\\u0020*$")), "z.string().regex(/^(?:a)\\u0020*$/)"),
        ["an empty array"] = (Z("tuple", new ArrayLiteral([])), "z.tuple([])"),
        ["a key that is not an identifier"] = (new Name("RowSchema").Then("extend", new ObjectLiteral([new ZodProperty("unit price", Z("never").Then("optional"))])), "RowSchema.extend({ \"unit price\": z.never().optional() })"),
        ["a note, which is not code"] = (new Noted(Z("string"), "written beside the property, not here"), "z.string()"),
    };

    public static TheoryData<string> FormNames => [.. Forms.Keys];

    [Theory]
    [MemberData(nameof(FormNames))]
    public void Each_expression_form_prints_as_typescript(string form) => Assert.Equal(Forms[form].Printed, ZodPrinter.Expression(Forms[form].Expression));

    [Fact]
    public void An_object_schema_puts_a_property_on_each_line_with_its_notes_and_getters()
    {
        var schema = new ObjectSchema(
            [
                new ZodProperty("id", Z("int32")),
                new ZodProperty("first name", new Noted(Z("string"), "ends */ here")),
                new ZodProperty("children", Z("array", new Name("NodeSchema")), Getter: true),
            ],
            Strict: false);

        Assert.Equal(
            "z.object({\n  id: z.int32(),\n  \"first name\": z.string() /* ends *\\/ here */,\n  get children() { return z.array(NodeSchema) },\n})",
            ZodPrinter.Expression(schema));
    }

    [Fact]
    public void A_strict_object_schema_is_a_strict_object() =>
        Assert.Equal("z.strictObject({\n})", ZodPrinter.Expression(new ObjectSchema([], Strict: true)));

    [Fact]
    public void A_module_prints_its_header_imports_helper_and_declarations()
    {
        var module = new ZodModule(
            ["First line.", "Second line."],
            [new ZodImport("./it's", ["A", "B"])],
            JsonTextHelper: true,
            [new ZodDeclaration("Get", "s.get: returns rows\n*/ evil.", Z("unknown"))]);

        var text = ZodPrinter.Print(module);

        Assert.StartsWith("// First line.\n// Second line.\nimport { z } from 'zod'\nimport { A, B } from './it\\'s'\n\n// JSON text:", text, StringComparison.Ordinal);
        Assert.EndsWith("\n/** s.get: returns rows *\\/ evil. */\nexport const GetSchema = z.unknown()\nexport type Get = z.infer<typeof GetSchema>\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_module_without_the_helper_goes_from_its_imports_to_its_declarations() =>
        Assert.Equal(
            "// H\nimport { z } from 'zod'\n\nexport const ASchema = z.boolean()\nexport type A = z.infer<typeof ASchema>\n",
            ZodPrinter.Print(new ZodModule(["H"], [], JsonTextHelper: false, [new ZodDeclaration("A", null, Z("boolean"))])));
}
