using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

// Names that differ only in case: two columns of a table, or two variables or temporary tables of a body, stop the run (HAR1022,
// decision 0119), so inference meets such names only where no reader sees them: the columns of a derived table, a CTE or a view,
// and of a temporary table SELECT … INTO makes from them. A database that compares names by case takes those, and there a
// reference to either is unknown, naming both (decision 0107), rather than read as the other.
public partial class ConstructTests
{
    /// <summary>
    /// Columns Code and code, and qty and Qty, that differ only in case, as a query on a database of Latin1_General_100_CS_AS may
    /// name them. Matched ignoring case, code would be read as Code's varchar(10) and Qty as qty's int that is never null.
    /// </summary>
    private const string CaseVariantRow = "SELECT CAST('x' AS varchar(10)) AS Code, CAST(NULL AS int) AS code, 2 AS qty, CAST(NULL AS int) AS Qty";

    [Theory]
    [InlineData($"SELECT d.code, d.Qty FROM ({CaseVariantRow}) AS d;")]
    [InlineData($"WITH d AS ({CaseVariantRow}) SELECT code, Qty FROM d;")]
    [InlineData($"SELECT x.code, x.Qty FROM (SELECT * FROM ({CaseVariantRow}) AS d) AS x;")]
    [InlineData($"SELECT * INTO #i FROM ({CaseVariantRow}) AS d; SELECT code, Qty FROM #i;")]
    public void A_column_whose_name_another_differs_from_only_in_case_is_unknown(string query)
    {
        var doc = Infer(("tables.sql", Tables), ("p.sql", $"CREATE PROCEDURE dbo.p AS\n{query}"));

        foreach (var name in new[] { "code", "Qty" })
            Assert.IsType<IrUnknownType>(Fields(doc).Single(f => f.Name == name).Type);
    }

    [Fact]
    public void A_view_whose_columns_differ_only_in_case_is_unknown()
    {
        // A view's columns are listed by name, and two that differ only in case can't be, so every column read from it is unknown.
        var doc = Infer(("tables.sql", $"CREATE VIEW dbo.v AS {CaseVariantRow};"), ("p.sql", "CREATE PROCEDURE dbo.p AS\nSELECT code, Qty, qty FROM dbo.v;"));

        Assert.All(Fields(doc), f => Assert.IsType<IrUnknownType>(f.Type));
    }
}
