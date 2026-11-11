using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

public partial class DmlTests
{
    [Theory]
    [InlineData("INSERT INTO dbo.item (id, label) VALUES (1, N'a');")]
    [InlineData("INSERT INTO dbo.item (id) SELECT i.id + 0 FROM dbo.item AS i;")]
    [InlineData("INSERT INTO dbo.note DEFAULT VALUES;")]
    [InlineData("INSERT dbo.note (text) VALUES (NULL);")]
    [InlineData("UPDATE dbo.item SET qty = 1 WHERE id = 1;")]
    [InlineData("UPDATE i SET label = n.text FROM dbo.item AS i JOIN dbo.note AS n ON n.id = i.id;")]
    [InlineData("DELETE FROM dbo.item WHERE id = 1;")]
    [InlineData("DELETE i FROM dbo.item AS i JOIN dbo.note AS n ON n.id = i.id;")]
    [InlineData("MERGE dbo.item AS t USING dbo.note AS s ON t.id = s.id WHEN MATCHED THEN UPDATE SET label = s.text WHEN NOT MATCHED THEN INSERT (id) VALUES (s.id) WHEN NOT MATCHED BY SOURCE THEN DELETE;")]
    [InlineData("TRUNCATE TABLE dbo.note;")]
    [InlineData("WITH c AS (SELECT n.id FROM dbo.note AS n) INSERT INTO dbo.item (id) SELECT c.id FROM c;")]
    [InlineData("WITH c AS (SELECT i.id, i.label FROM dbo.item AS i) UPDATE c SET label = N'x';")]
    [InlineData("WITH c AS (SELECT i.id FROM dbo.item AS i) DELETE FROM c;")]
    [InlineData("WITH c AS (SELECT n.id FROM dbo.note AS n) MERGE dbo.item AS t USING c ON t.id = c.id WHEN MATCHED THEN DELETE;")]
    public void Dml_on_a_permanent_table_returns_no_result_set(string dml)
    {
        // Observed: Dml_on_a_permanent_table_returns_no_result_set.
        var doc = Doc(("p", $"CREATE PROCEDURE dbo.p AS SELECT i.id AS a FROM dbo.item AS i; {dml} SELECT i.qty AS b FROM dbo.item AS i;"));

        var results = Results(doc);
        Assert.Equal(2, results.Count);
        Assert.Equal(["a"], Names(doc, results[0].Type));
        Assert.Equal(["b"], Names(doc, results[1].Type));
    }

    [Fact]
    public void Insert_exec_into_a_permanent_table_returns_none_of_the_callees_result_sets()
    {
        var doc = Doc(
            ("q", "CREATE PROCEDURE dbo.q AS SELECT n.text FROM dbo.note AS n;"),
            ("p", "CREATE PROCEDURE dbo.p AS INSERT INTO dbo.note (text) EXEC dbo.q; SELECT i.id FROM dbo.item AS i;"));

        Assert.Equal(["id"], Names(doc, Assert.Single(Results(doc)).Type));
    }

    [Fact]
    public void A_read_whose_catch_calls_a_logger_that_inserts_is_typed()
    {
        // The pattern that left a whole corpus untyped: every procedure's CATCH calls a shared logger that writes a table.
        var doc = Doc(
            ("log", "CREATE PROCEDURE dbo.log_error @message nvarchar(4000) AS SET NOCOUNT ON; INSERT INTO dbo.error_log (message, at) VALUES (@message, SYSUTCDATETIME());"),
            ("p", """
                CREATE PROCEDURE dbo.p AS
                BEGIN TRY
                    SELECT i.id, i.label FROM dbo.item AS i;
                END TRY
                BEGIN CATCH
                    DECLARE @m nvarchar(4000) = ERROR_MESSAGE();
                    EXEC dbo.log_error @m;
                    THROW;
                END CATCH
                """));

        var result = Assert.Single(Results(doc));
        Assert.Equal(["id", "label"], Names(doc, result.Type));
        Assert.False(Field(doc, "id").Nullable);
        Assert.Empty(Results(doc, "dbo.log_error"));
    }
}
