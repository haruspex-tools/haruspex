using Haruspex.Emit.Zod;
using Haruspex.Ir;

namespace Haruspex.Tests.Inference;

public partial class JsonFunctionTests
{
    [Fact]
    public void The_ir_writes_the_recursive_flag_and_json_text_content()
    {
        var json = IrJsonWriter.Write(Infer(Tree, "SELECT dbo.tree(t.id) AS text, json_query(dbo.tree(t.id)) AS live FROM dbo.t AS t FOR JSON PATH;"));

        Assert.Contains("\"TreeNode\": {\n      \"kind\": \"object\",\n      \"recursive\": true,", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\": \"json-text\",\n", json, StringComparison.Ordinal);
        Assert.Contains("\"content\": {", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Zod_writes_a_recursive_reference_as_a_getter()
    {
        var zod = ZodEmitter.Emit(Infer(Tree, "SELECT json_query(dbo.tree(t.id)) AS children FROM dbo.t AS t FOR JSON PATH;")).Expected();

        Assert.Contains("export const TreeNodeSchema = z.object({\n", zod, StringComparison.Ordinal);
        Assert.Contains("  get children() { return z.array(TreeNodeSchema).min(1).optional() },\n", zod, StringComparison.Ordinal);
        Assert.Contains("  id: z.int32(),\n", zod, StringComparison.Ordinal);
    }

    [Fact]
    public void Zod_checks_json_text_against_its_content_and_keeps_it_a_string()
    {
        var zod = ZodEmitter.Emit(Infer(Children, "SELECT dbo.children(t.id) AS kids FROM dbo.t AS t FOR JSON PATH;")).Expected();

        Assert.Contains("const jsonText = (content: () => z.ZodType): z.ZodString =>", zod, StringComparison.Ordinal);
        Assert.Contains("  /** A string holding JSON, checked against the schema inside. */\n  kids: jsonText(() => z.array(PKidsSchema).min(1)).optional(),\n", zod, StringComparison.Ordinal);
    }

    [Fact]
    public void Zod_writes_no_helper_when_no_json_text_has_content() =>
        Assert.DoesNotContain("jsonText", ZodEmitter.Emit(Infer(Children, "SELECT json_query(dbo.children(t.id)) AS kids FROM dbo.t AS t FOR JSON PATH;")).Expected(), StringComparison.Ordinal);

    [Fact]
    public void Zod_writes_every_reference_within_a_cycle_as_a_getter()
    {
        var zod = ZodEmitter.Emit(Infer(Categories, "SELECT json_query(dbo.categories(t.id)) AS tree FROM dbo.t AS t FOR JSON PATH;")).Expected();

        // Whichever is written first, each refers to the other inside the cycle, so both are getters; the row, outside
        // the cycle, refers to it plainly.
        Assert.Contains("  get subcategories() { return z.array(CategoriesNodeSchema).min(1).optional() },\n", zod, StringComparison.Ordinal);
        Assert.Contains("  get items() { return ItemsNodeSchema },\n", zod, StringComparison.Ordinal);
        Assert.Contains("  tree: z.array(CategoriesNodeSchema).min(1).optional(),\n", zod, StringComparison.Ordinal);
    }
}
