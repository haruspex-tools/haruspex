using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

// $IDENTITY and $ROWGUID in a select list, named as SQL Server names them (observed: Identity_is_named_after_the_tables_identity_column).
public partial class ConstructTests
{
    private const string Orders = """
        CREATE TABLE dbo.orders (order_id int IDENTITY(1, 1) NOT NULL PRIMARY KEY, row_guid uniqueidentifier ROWGUIDCOL NOT NULL DEFAULT NEWID(), note varchar(20) NULL);
        GO
        CREATE TABLE dbo.counted (counted_id int IDENTITY(1, 1) NOT NULL);
        GO
        """;

    private static IrDocument OrdersProcedure(string body) => Infer(("tables.sql", Orders), ("p.sql", $"CREATE PROCEDURE dbo.p AS\n{body}"));

    [Theory]
    [InlineData("SELECT o.$identity, o.note FROM dbo.orders AS o;")]
    [InlineData("SELECT dbo.orders.$identity, dbo.orders.note FROM dbo.orders;")]
    [InlineData("SELECT $identity, note FROM dbo.orders;")]
    [InlineData("SELECT d.* FROM (SELECT o.$identity, o.note FROM dbo.orders AS o) AS d;")]
    public void Identity_is_named_after_the_identity_column_of_the_table_it_reads(string body)
    {
        var doc = OrdersProcedure(body);

        // Named, never after the qualifier: its value is still not read, so it is unknown under the table's column's name.
        Assert.Equal(["order_id", "note"], RowFields(doc).Select(f => f.Name));
        Assert.DoesNotContain(doc.Unknowns, u => u.Path.EndsWith("].o", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("SELECT o.$rowguid FROM dbo.orders AS o;", "ROWGUIDCOL")]
    [InlineData("SELECT $identity FROM dbo.orders CROSS JOIN dbo.counted;", "identity column")]
    [InlineData("SELECT d.$identity FROM (SELECT order_id FROM dbo.orders) AS d;", "identity column")]
    public void A_pseudo_column_whose_column_is_not_known_leaves_the_result_unknown_saying_the_table_names_it(string body, string named)
    {
        // The model doesn't record ROWGUIDCOL; two tables with an identity column make a bare one ambiguous (209); a derived
        // table's is not followed. None is named after its qualifier.
        var unknown = Unknown(OrdersProcedure(body), "results[0]");

        Assert.Equal(IrUnknownReason.UnnamedColumn, unknown.Reason);
        Assert.Contains(named, unknown.Detail, StringComparison.Ordinal);
    }
}
