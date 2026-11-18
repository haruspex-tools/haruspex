using Haruspex.DifferentialTests.Server;

namespace Haruspex.DifferentialTests;

// Computed columns: a column whose value is its expression, PERSISTED or not. Inference's README.md, "Computed columns", gives
// the rules these rest on.
public partial class ExpressionObservationTests
{
    /// <summary>A table of computed columns over a never-null int, a nullable int and a string.</summary>
    private const string Computed = """
        CREATE TABLE dbo.obs_cc (
            id int NOT NULL PRIMARY KEY, a int NOT NULL, b int NULL, s varchar(10) NOT NULL,
            k AS (CASE WHEN a > 5 THEN 1 ELSE 0 END), p AS (CAST(a AS tinyint)) PERSISTED, v AS (a * 2), n AS (b + 1),
            m AS (PARSENAME(s, 1)), w AS (CONCAT(s, 'x')) PERSISTED NOT NULL);
        CREATE TABLE dbo.obs_cq (id int NOT NULL PRIMARY KEY, b int NULL, q AS (b * 2) PERSISTED NOT NULL);
        """;

    [Fact]
    public async Task A_computed_column_is_of_its_expressions_type_and_null_as_its_expression_is_persisted_or_not()
    {
        // The server describes each computed column as its expression: the type, and NULL only where the expression may be. A
        // conversion or arithmetic that may overflow is nullable; so is b + 1 of a nullable b. PERSISTED NOT NULL is described by
        // its expression too (q), though no NULL is ever stored in it (below).
        Assert.Equal(
            "k int not null, p tinyint null, v int null, n int null, m nvarchar(128) null, w varchar(11) not null, q int null",
            await FirstResultSet.DescribeNullability(server, "SELECT k, p, v, n, m, w, q FROM dbo.obs_cc CROSS JOIN dbo.obs_cq", [Computed]));

        // Read, each is its expression's value; NULL where its expression is.
        Assert.Equal(
            "k,p,v,n,m,w: 0,3,6,null,x,xx | k,p,v,n,m,w: 1,10,20,8,a,b.ax",
            (await ObservedCall.Run(server, [Computed, "INSERT INTO dbo.obs_cc (id, a, b, s) VALUES (1, 3, NULL, 'x'), (2, 10, 7, 'b.a');"],
                "SELECT k, p, v, n, m, w FROM dbo.obs_cc WHERE id = 1; SELECT k, p, v, n, m, w FROM dbo.obs_cc WHERE id = 2;")).ToString());

        // PERSISTED NOT NULL is checked when a row is written (515), so a stored value is never null.
        Assert.Equal(515, (await ObservedCall.Run(server, [Computed], "INSERT INTO dbo.obs_cq (id, b) VALUES (1, NULL);")).Error);
    }

    [Fact]
    public async Task A_persisted_computed_column_is_written_only_with_ansi_warnings_on_and_a_computed_column_reads_no_other()
    {
        // A row of a table with a PERSISTED computed column can't be written under ANSI_WARNINGS off (1934), so its stored value
        // never came from an overflow made NULL; read, a computed column that isn't persisted is computed under the reader's options.
        Assert.Equal(1934, (await ObservedCall.Run(server, [Computed], "SET ANSI_WARNINGS OFF; INSERT INTO dbo.obs_cc (id, a, b, s) VALUES (1, 300, 1, 'x');")).Error);
        Assert.Equal(
            "v: null",
            (await ObservedCall.Run(server, ["CREATE TABLE dbo.obs_cv (id int NOT NULL PRIMARY KEY, a int NOT NULL, v AS (CAST(a AS tinyint)));", "INSERT INTO dbo.obs_cv (id, a) VALUES (1, 300);"],
                "SET ANSI_WARNINGS OFF; SET ARITHABORT OFF; SELECT v FROM dbo.obs_cv;")).ToString());

        // A computed column can't read another (1759).
        Assert.Equal(1759, (await ObservedCall.Run(server, [], "CREATE TABLE dbo.obs_c2 (a int NOT NULL, b AS (a + 1), c AS (b + 1));")).Error);
    }

    [Fact]
    public async Task A_computed_column_compares_with_null_by_its_tables_ansi_nulls_whatever_the_readers()
    {
        // Created under ANSI_NULLS off, a = NULL is TRUE for a NULL a, so the CASE gives that NULL; under on, never. Either way
        // round, the reader's own setting doesn't matter: the table's, when it was created, does.
        string[] off = ["SET ANSI_NULLS OFF;", "CREATE TABLE dbo.obs_an (id int NOT NULL PRIMARY KEY, a int NULL, c AS (CASE WHEN a = NULL THEN a ELSE 0 END));", "SET ANSI_NULLS ON;", "INSERT INTO dbo.obs_an (id, a) VALUES (1, NULL);"];
        string[] on = ["CREATE TABLE dbo.obs_an (id int NOT NULL PRIMARY KEY, a int NULL, c AS (CASE WHEN a = NULL THEN a ELSE 0 END));", "INSERT INTO dbo.obs_an (id, a) VALUES (1, NULL);"];
        Assert.Equal("c: null", (await ObservedCall.Run(server, off, "SET ANSI_NULLS ON; SELECT c FROM dbo.obs_an;")).ToString());
        Assert.Equal("c: null", (await ObservedCall.Run(server, off, "SET ANSI_NULLS OFF; SELECT c FROM dbo.obs_an;")).ToString());
        Assert.Equal("c: 0", (await ObservedCall.Run(server, on, "SET ANSI_NULLS OFF; SELECT c FROM dbo.obs_an;")).ToString());
        Assert.Equal("c: 0", (await ObservedCall.Run(server, on, "SET ANSI_NULLS ON; SELECT c FROM dbo.obs_an;")).ToString());
    }

    [Fact]
    public async Task A_computed_column_of_json_nests_as_json_in_for_json()
    {
        // A column computed with JSON_OBJECT or JSON_QUERY is written as JSON, not as a string, by FOR JSON.
        Assert.Equal(
            """[{"id":1,"j":{"a":1},"q":{"b":1}}]""",
            (await ObservedCall.Run(server, ["CREATE TABLE dbo.obs_cj (id int NOT NULL PRIMARY KEY, j AS (JSON_OBJECT('a':id)), q AS (JSON_QUERY(N'{\"b\":1}')));", "INSERT INTO dbo.obs_cj (id) VALUES (1);"],
                "SELECT (SELECT id, j, q FROM dbo.obs_cj FOR JSON PATH) AS doc;")).ToString().Replace("doc: ", "", StringComparison.Ordinal));
    }
}
