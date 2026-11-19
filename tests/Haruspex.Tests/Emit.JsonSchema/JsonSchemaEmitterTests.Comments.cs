using Haruspex.Emit.JsonSchema;
using Haruspex.Ir;

namespace Haruspex.Tests.Emit.JsonSchema;

/// <summary>A surprise's property ends its description with the lines the Zod module's comment gives (<c>"comments"</c>).</summary>
public partial class JsonSchemaEmitterTests
{
    [Fact]
    public void A_surprise_s_description_ends_with_why_it_is_what_it_is()
    {
        var document = InferProcedure.Infer(
            "shop.*",
            ("tables.sql", "CREATE TABLE shop.order_line (line_id int NOT NULL PRIMARY KEY, order_id int NOT NULL);"),
            ("procedures.sql", "CREATE PROCEDURE shop.line_count AS SELECT l.order_id, count(l.line_id) AS lines FROM shop.order_line AS l GROUP BY l.order_id;"));

        var lines = Definition(Emit(document, new JsonSchemaOptions()), "LineCountRow")["properties"]!["lines"]!["description"]!.GetValue<string>();
        var none = Definition(Emit(document, new JsonSchemaOptions { Comments = IrComments.None }), "LineCountRow")["properties"]!["lines"]!["description"]!.GetValue<string>();

        Assert.EndsWith("ANSI_WARNINGS and ARITHABORT off (count-overflow). To change it: use COUNT_BIG, or declare the callers' ANSI_WARNINGS or ARITHABORT on in the config file's \"session\". Why: haruspex explain shop.line_count lines", lines, StringComparison.Ordinal);
        Assert.DoesNotContain("Why:", none, StringComparison.Ordinal);
    }
}
