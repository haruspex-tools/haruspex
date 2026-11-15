using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

// SELECT * in a function's body: an inline function's query, a multi-statement function's inserts, a scalar function's
// FOR JSON. Each is expanded against the tables as the project defines them (decision 0047).
public partial class SelectStarTests
{
    [Fact]
    public void Select_star_in_an_inline_function_is_expanded()
    {
        // An_inline_functions_star_is_bound_when_it_is_created, as a view's is: haruspex assumes it is bound against the
        // tables as the project defines them.
        var doc = Procedure(
            "SELECT f.* FROM shop.lines_of(@id) AS f;",
            "@id int",
            "CREATE FUNCTION shop.lines_of(@order int) RETURNS TABLE AS RETURN SELECT * FROM shop.order_line AS l WHERE l.order_id = @order;");

        AllTyped(doc);
        Assert.Equal(["line_id", "order_id", "sku", "qty"], Names(doc));
    }

    [Fact]
    public void Select_star_in_a_multi_statement_functions_insert_writes_every_column()
    {
        // A_multi_statement_functions_star_is_bound_when_it_runs.
        var doc = Procedure(
            "SELECT f.qty FROM shop.counted() AS f;",
            others: """
                CREATE FUNCTION shop.counted() RETURNS @r TABLE (line_id int NOT NULL, order_id int NOT NULL, sku varchar(12) NOT NULL, qty int NULL) AS
                BEGIN
                    INSERT INTO @r SELECT * FROM shop.order_line AS l WHERE l.qty IS NOT NULL;
                    RETURN;
                END;
                """);

        AllTyped(doc);
    }

    [Fact]
    public void Select_star_in_a_scalar_functions_for_json_has_a_key_per_column()
    {
        // A_scalar_functions_star_is_bound_when_it_runs.
        var doc = Procedure(
            "SELECT shop.card(@id) AS card;",
            "@id int",
            "CREATE FUNCTION shop.card(@id int) RETURNS nvarchar(max) AS BEGIN RETURN (SELECT * FROM shop.orders AS o WHERE o.order_id = @id FOR JSON PATH, WITHOUT_ARRAY_WRAPPER); END;");

        AllTyped(doc);
        var card = Field(doc, "card").Type;
        var content = Assert.IsType<IrRef>(Assert.IsType<IrJsonText>(card).Content);
        Assert.Equal(["order_id", "customer", "placed_on", "note"], Fields(doc, content).Select(f => f.Name));
    }
}
