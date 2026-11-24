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
    public void A_column_added_beside_one_that_differs_from_it_only_in_case_is_unknown()
    {
        // ALTER TABLE … ADD isn't applied to the model, so the table it changes is unknown, and its pair is never matched.
        var doc = Infer(
            ("tables.sql", "CREATE TABLE dbo.item (id int NOT NULL, Code varchar(10) NOT NULL);\nGO\nALTER TABLE dbo.item ADD code int NULL;"),
            ("p.sql", "CREATE PROCEDURE dbo.p AS\nSELECT code FROM dbo.item;"));

        Assert.IsType<IrUnknownType>(Assert.Single(Fields(doc)).Type);
    }

    [Fact]
    public void A_view_whose_columns_differ_only_in_case_is_unknown()
    {
        // A view's columns are listed by name, and two that differ only in case can't be, so every column read from it is unknown.
        var doc = Infer(("tables.sql", $"CREATE VIEW dbo.v AS {CaseVariantRow};"), ("p.sql", "CREATE PROCEDURE dbo.p AS\nSELECT code, Qty, qty FROM dbo.v;"));

        Assert.All(Fields(doc), f => Assert.IsType<IrUnknownType>(f.Type));
    }

    [Theory]
    // A callee that creates #T and reads #t reads its own #T on a case-insensitive server and its caller's #t on a case-sensitive
    // one, so the call is followed neither way (decision 0119). A callee that only reads #T, or creates #t, is followed as before.
    [InlineData("CREATE TABLE #T (y varchar(5) NULL); INSERT #T VALUES ('in'); SELECT * FROM #t;", true)]
    [InlineData("SELECT 1 AS one INTO #T; SELECT * FROM #t;", true)]
    [InlineData("CREATE TABLE #t (y varchar(5) NULL); INSERT #t VALUES ('in'); SELECT * FROM #t;", false)]
    [InlineData("SELECT * FROM #T;", false)]
    public void A_callee_that_creates_a_temporary_table_whose_name_differs_only_in_case_from_its_caller_s_is_not_followed(string inner, bool unknown)
    {
        var doc = InferProcedure.Infer(
            "dbo.outer_p",
            ("inner.sql", $"CREATE PROCEDURE dbo.inner_p AS\n{inner}"),
            ("outer.sql", "CREATE PROCEDURE dbo.outer_p AS\nCREATE TABLE #t (x int NOT NULL); INSERT #t VALUES (1); EXEC dbo.inner_p;"));

        Assert.Equal(unknown, doc.Unknowns.Any(u => u.Detail.Contains("differs only in case from the caller's #t", StringComparison.Ordinal)));
        if (!unknown)
            Assert.NotEmpty(Fields(doc));
    }
}
