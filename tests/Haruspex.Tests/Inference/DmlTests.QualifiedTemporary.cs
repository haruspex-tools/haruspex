using Haruspex.Ir;

namespace Haruspex.Tests.Inference;

// A temporary table named with a schema, a database or a server is the session's temporary table: SQL Server ignores every part
// but the last (observed: A_temporary_table_named_with_a_schema_database_or_server_is_the_sessions_own), so a write through such
// a name is a write to it.
public partial class DmlTests
{
    [Theory]
    [InlineData("INSERT INTO dbo.#t (hr, n) VALUES (1, NULL);")]
    [InlineData("UPDATE tempdb..#t SET n = NULL;")]
    [InlineData("UPDATE nowhere.#t SET n = NULL;")]
    [InlineData("INSERT srv.tempdb.dbo.#t (hr, n) VALUES (1, NULL);")]
    [InlineData("MERGE dbo.#t AS t USING (SELECT 1 AS hr) AS s ON t.hr = s.hr WHEN MATCHED THEN UPDATE SET n = NULL;")]
    [InlineData("WITH c AS (SELECT t.n FROM dbo.#t AS t) UPDATE c SET n = NULL;")]
    [InlineData("UPDATE dbo.item SET qty = qty OUTPUT 1, NULL INTO master.dbo.#t (hr, n);")]
    public void A_write_through_a_temporary_tables_qualified_name_reaches_the_table(string write)
    {
        var doc = Doc(("p", $"""
            CREATE PROCEDURE dbo.p AS
            SELECT i.id AS hr, i.qty AS n INTO #t FROM dbo.item AS i WHERE i.qty > 0;
            {write}
            SELECT h.n FROM #t AS h;
            """));

        var n = Field(doc, "n");
        Assert.True(n.Nullable || n.Type is IrUnknownType, $"n is claimed never null after: {write}");
    }

    [Theory]
    [InlineData("CREATE TABLE nowhere.#t (n int NOT NULL);", "SELECT h.n FROM tempdb..#t AS h;")]
    [InlineData("SELECT i.qty AS n INTO dbo.#t FROM dbo.item AS i WHERE i.qty > 0;", "SELECT h.n FROM #t AS h;")]
    [InlineData("CREATE TABLE #t (n int NOT NULL);", "SELECT dbo.#t.n FROM master.dbo.#t;")]
    public void A_temporary_table_made_or_read_through_a_qualified_name_is_the_sessions_own(string made, string read)
    {
        var doc = Doc(("p", $"""
            CREATE PROCEDURE dbo.p AS
            {made}
            {read}
            """));

        Assert.Empty(doc.Unknowns);
        Assert.False(Field(doc, "n").Nullable);
    }
}
