using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

/// <summary>
/// JSON built in the project, traced to where it is read: through variables, table variables, temporary tables and
/// multi-statement functions, as a JSON literal, and under json_query, OPENJSON and JSON_VALUE. Each rule rests on an observation
/// in the differential tests' <c>JsonTracingObservationTests</c>, named where it matters.
/// </summary>
public partial class JsonTracingTests
{
    private const string Tables = """
        CREATE TABLE sales.[order] (order_id int NOT NULL PRIMARY KEY, total decimal(9,2) NOT NULL, notes nvarchar(max) NULL);
        GO
        CREATE TABLE sales.order_line (line_id int NOT NULL PRIMARY KEY, order_id int NOT NULL, sku varchar(8) NOT NULL, qty int NOT NULL,
            colour nvarchar(10) NULL);
        GO
        """;

    private static IrDocument Infer(string body, string parameters = "@id int, @flag bit, @raw nvarchar(max)", string more = "") =>
        InferProcedure.Infer(
            "sales.p",
            [("schema.sql", "CREATE SCHEMA sales;\nGO\n"), ("tables.sql", Tables), .. more.Length > 0 ? [("more.sql", more)] : Array.Empty<(string, string)>(), ("p.sql", $"CREATE PROCEDURE sales.p {parameters} AS\n{body}")]);

    private const string Lines = "DECLARE @lines nvarchar(max) = (SELECT l.sku, l.qty FROM sales.order_line AS l WHERE l.order_id = @id FOR JSON PATH);\n";

    private static IrField Sub(IrDocument doc, IrType type, string name) => Assert.Single(Fields(doc, type), f => f.Name == name);

    // Unlike IrNavigation.Unknown, found by the field's name at the end of its path.
    private static IrUnknown Unknown(IrDocument doc, string field) => Assert.Single(doc.Unknowns, u => u.Path.EndsWith("." + field, StringComparison.Ordinal));

    /// <summary>The lines' item type: an object of sku and qty, each never null.</summary>
    private static void AssertLines(IrDocument doc, IrType type)
    {
        var items = Assert.IsType<IrArray>(type).Items;
        Assert.Equal(["sku", "qty"], Fields(doc, items).Select(f => f.Name));
        Assert.All(Fields(doc, items), f => Assert.False(f.Nullable));
    }
}
