namespace Haruspex.DifferentialTests;

// $IDENTITY and $ROWGUID in a select list: the names SQL Server gives them, which Binder.ColumnName follows.
public partial class BindingObservationTests
{
    /// <summary>A table with an identity column and a ROWGUIDCOL, and two more to read beside it.</summary>
    private const string Identities = """
        CREATE TABLE dbo.obs_orders (order_id int IDENTITY(1, 1) NOT NULL PRIMARY KEY, row_guid uniqueidentifier ROWGUIDCOL NOT NULL DEFAULT NEWID(), note varchar(20) NULL);
        INSERT INTO dbo.obs_orders (note) VALUES ('a');
        CREATE TABLE dbo.obs_plain (p int NOT NULL); INSERT INTO dbo.obs_plain VALUES (5);
        CREATE TABLE dbo.obs_counted (counted_id int IDENTITY(1, 1) NOT NULL); INSERT INTO dbo.obs_counted DEFAULT VALUES;
        """;

    [Theory]
    [InlineData("SELECT o.$identity, o.note FROM dbo.obs_orders AS o;", "order_id,note: 1,a")]
    [InlineData("SELECT dbo.obs_orders.$identity FROM dbo.obs_orders;", "order_id: 1")]
    [InlineData("SELECT $identity FROM dbo.obs_orders;", "order_id: 1")]
    [InlineData("SELECT $identity, p FROM dbo.obs_orders CROSS JOIN dbo.obs_plain;", "order_id,p: 1,5")]
    [InlineData("SELECT d.* FROM (SELECT o.$identity, o.note FROM dbo.obs_orders AS o) AS d;", "order_id,note: 1,a")]
    [InlineData("SELECT o.$identity AS renamed FROM dbo.obs_orders AS o;", "renamed: 1")]
    // $IDENTITY is named after the table's identity column, never after its qualifier, unless it has an alias; bare, it is the
    // identity column of the one source that has one.
    public async Task Identity_is_named_after_the_tables_identity_column(string query, string outcome) =>
        Assert.Equal(outcome, (await Run([Identities], query)).ToString());

    [Fact]
    public async Task Rowguid_is_named_after_the_tables_rowguidcol() =>
        // Its value is new each run, so the name is read from the description of the result.
        Assert.Equal(
            "name: order_id;row_guid",
            (await Run([Identities], "SELECT name FROM sys.dm_exec_describe_first_result_set(N'SELECT o.$identity, o.$rowguid FROM dbo.obs_orders AS o;', NULL, 0) ORDER BY column_ordinal;")).ToString());

    [Theory]
    [InlineData("SELECT $identity FROM dbo.obs_orders CROSS JOIN dbo.obs_counted;", 209)]
    [InlineData("SELECT p.$identity FROM dbo.obs_plain AS p;", 207)]
    // Bare, with two sources that have one, it is ambiguous; of a table without one, it is no column.
    public async Task Identity_of_two_tables_or_of_none_is_refused(string query, int error) =>
        Assert.Equal(error, (await Run([Identities], query)).Error);
}
