using Haruspex.Emit.Zod;
using Haruspex.Ir;

namespace Haruspex.Tests.Emit.Zod;

/// <summary>
/// The comments that say why a field is what it is (<c>"comments"</c>): above each surprise's property by default, none under
/// <see cref="IrComments.None"/>, and, for a type whose shape another shares, above its export, so that comments never stop a
/// shape being shared.
/// </summary>
public partial class ZodEmitterTests
{
    private const string OrderTables = """
        CREATE TABLE shop.customer_order (order_id int NOT NULL PRIMARY KEY, note nvarchar(40) NULL);
        GO
        CREATE TABLE shop.order_line (line_id int NOT NULL PRIMARY KEY, order_id int NOT NULL);
        """;

    private static IrDocument Orders(string procedures) => InferProcedure.Infer("shop.*", ("tables.sql", OrderTables), ("procedures.sql", procedures));

    private const string LineCount = "CREATE PROCEDURE shop.line_count AS SELECT o.order_id, count(l.line_id) AS lines FROM shop.customer_order AS o LEFT JOIN shop.order_line AS l ON l.order_id = o.order_id GROUP BY o.order_id;";

    [Fact]
    public void A_surprise_s_property_says_why_above_it_and_a_field_as_its_column_declares_says_nothing()
    {
        var ts = ZodEmitter.Emit(Orders(LineCount)).Expected();

        Assert.Contains(
            """
              /**
               * May be null: COUNT is an int, null past 2,147,483,647 rows when the caller's session has ANSI_WARNINGS and ARITHABORT off (count-overflow).
               * To change it: use COUNT_BIG, or declare the callers' ANSI_WARNINGS or ARITHABORT on in the config file's "session".
               * Why: haruspex explain shop.line_count lines
               */
              lines: z.int32().nullable(),
            """.ReplaceLineEndings("\n"),
            ts,
            StringComparison.Ordinal);
        Assert.Contains("({\n  order_id: z.int32(),\n", ts, StringComparison.Ordinal);
    }

    [Fact]
    public void Under_none_no_explanation_is_written_and_under_all_every_field_has_one()
    {
        var document = Orders(LineCount);

        Assert.DoesNotContain("Why:", ZodEmitter.Emit(document, new ZodOptions { Comments = IrComments.None }).Expected(), StringComparison.Ordinal);
        Assert.Contains("   * Read from shop.customer_order.order_id.\n   * Why: haruspex explain shop.line_count order_id\n   */\n  order_id: z.int32(),", ZodEmitter.Emit(document, new ZodOptions { Comments = IrComments.All }).Expected(), StringComparison.Ordinal);
    }

    [Fact]
    public void Types_that_share_a_shape_still_share_it_and_each_says_its_own_reasons_above_its_export()
    {
        // The same shape, null for a different reason in each: a nullable column, and an outer join.
        var document = Orders("""
            CREATE PROCEDURE shop.notes AS SELECT o.note FROM shop.customer_order AS o;
            GO
            CREATE PROCEDURE shop.joined AS SELECT o.note FROM shop.order_line AS l LEFT JOIN shop.customer_order AS o ON o.order_id = l.order_id;
            """);

        var ts = ZodEmitter.Emit(document).Expected();

        Assert.Contains("export const JoinedRowSchema = shape", ts, StringComparison.Ordinal);
        Assert.Contains("export const NotesRowSchema = shape", ts, StringComparison.Ordinal);
        Assert.Contains(
            "/**\n * note: May be null: it comes from the optional side of an outer join, which has no row when nothing matches (unmatched-row).\n",
            ts,
            StringComparison.Ordinal);
        Assert.Contains("note: Why: haruspex explain shop.joined note", ts, StringComparison.Ordinal);
        Assert.DoesNotContain("haruspex explain shop.notes", ts, StringComparison.Ordinal);
    }
}
