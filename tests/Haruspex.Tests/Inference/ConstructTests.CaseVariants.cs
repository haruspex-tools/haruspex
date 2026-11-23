using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

// Names that differ only in case (decision 0107): two such columns or variables can exist only where the database compares names
// by case, which haruspex doesn't model, so a reference to either is unknown, naming both, rather than read as the other.
public partial class ConstructTests
{
    /// <summary>
    /// A table whose columns Code and code, and qty and Qty, differ only in case, as a database of Latin1_General_100_CS_AS allows.
    /// Matched ignoring case, code would be read as Code's varchar(10) and Qty with qty's CHECK and nullability.
    /// </summary>
    private const string CaseSensitiveItem = """
        CREATE TABLE dbo.item (id int NOT NULL PRIMARY KEY, Code varchar(10) NOT NULL, code int NULL, qty int NULL CHECK (qty BETWEEN 1 AND 5), Qty int NOT NULL);
        """;

    [Theory]
    [InlineData("SELECT code, Qty FROM dbo.item;")]
    [InlineData("SELECT i.code, i.Qty FROM dbo.item AS i;")]
    [InlineData("SELECT * FROM dbo.item;")]
    [InlineData("SELECT x.code, x.Qty FROM (SELECT * FROM dbo.item) AS x;")]
    [InlineData("SELECT * INTO #i FROM dbo.item; SELECT code, Qty FROM #i;")]
    public void A_column_whose_name_another_differs_from_only_in_case_is_unknown(string query)
    {
        var doc = Infer(("tables.sql", CaseSensitiveItem), ("p.sql", $"CREATE PROCEDURE dbo.p AS\n{query}"));

        foreach (var name in new[] { "code", "Qty" })
        {
            var field = Fields(doc).Single(f => f.Name == name);
            Assert.IsType<IrUnknownType>(field.Type);
        }

        if (Fields(doc).SingleOrDefault(f => f.Name == "id") is { } id)
            Assert.IsNotType<IrUnknownType>(id.Type);
    }

    [Fact]
    public void A_column_whose_name_another_differs_from_only_in_case_gives_both_names_as_the_reason()
    {
        var doc = Infer(("tables.sql", CaseSensitiveItem), ("p.sql", "CREATE PROCEDURE dbo.p AS\nSELECT Code FROM dbo.item;"));

        Assert.Contains(doc.Unknowns, u => u.Detail.Contains("Code and code", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("CREATE TABLE #t (a int NOT NULL, A varchar(5) NULL); INSERT INTO #t VALUES (1, 'x'); SELECT a, A FROM #t;")]
    [InlineData("DECLARE @t TABLE (a int NOT NULL, A varchar(5) NULL); INSERT INTO @t VALUES (1, 'x'); SELECT * FROM @t;")]
    public void A_body_s_own_tables_columns_that_differ_only_in_case_are_unknown(string body)
    {
        var doc = Procedure(body);

        Assert.All(Fields(doc), f => Assert.IsType<IrUnknownType>(f.Type));
    }

    [Theory]
    [InlineData("@Total int", "DECLARE @total varchar(10) = 'x'; SELECT @Total AS t, @total AS u;")]
    [InlineData("", "DECLARE @n int = 1; DECLARE @N varchar(10) = 'x'; SELECT @n AS t, @N AS u;")]
    [InlineData("", "DECLARE @T TABLE (a int NOT NULL); DECLARE @t TABLE (a varchar(5) NULL); SELECT x.a AS t, x.a AS u FROM @t AS x;")]
    public void A_variable_whose_name_another_differs_from_only_in_case_is_unknown(string parameters, string body)
    {
        var doc = Infer(("tables.sql", Tables), ("p.sql", $"CREATE PROCEDURE dbo.p {parameters}\nAS\n{body}"));

        Assert.All(Fields(doc), f => Assert.IsType<IrUnknownType>(f.Type));
        Assert.Contains(doc.Unknowns, u => u.Detail.Contains("differ only in case", StringComparison.Ordinal));
    }
}
