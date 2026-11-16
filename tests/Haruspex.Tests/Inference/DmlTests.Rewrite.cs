using Haruspex.Ir;

namespace Haruspex.Tests.Inference;

// UPDATE … SET c.WRITE(…) of the body's own tables. Observed:
// Write_changes_part_of_a_value_in_place_keeping_its_type_and_fails_on_null.
public partial class DmlTests
{
    [Fact]
    public void Write_on_a_declared_temporary_table_leaves_its_columns_as_declared()
    {
        var doc = Doc(("p", """
            CREATE PROCEDURE dbo.p AS
            CREATE TABLE #t (id int NOT NULL, body nvarchar(max) NOT NULL, note nvarchar(max) NULL);
            INSERT #t (id, body) SELECT i.id, N'x' FROM dbo.item AS i;
            UPDATE #t SET body.WRITE(N'y', 0, 1);
            SELECT t.id, t.body, t.note FROM #t AS t;
            """));

        Assert.Empty(doc.Unknowns);
        Assert.False(Field(doc, "body").Nullable);
        Assert.True(Field(doc, "note").Nullable);
        Assert.Equal("nvarchar(max)", Assert.IsType<IrScalar>(Field(doc, "body").Type).Sql);
    }

    [Fact]
    public void Write_on_a_column_select_into_made_keeps_its_type_and_nulls_but_only_its_types_facts()
    {
        // The column held one value, N'open'; .WRITE makes it any string of nvarchar(max), never null where it wasn't.
        var doc = Doc(("p", """
            CREATE PROCEDURE dbo.p AS
            SELECT i.id, CAST(N'open' AS nvarchar(max)) AS state, CAST(i.label AS nvarchar(max)) AS label INTO #t FROM dbo.item AS i;
            UPDATE #t SET state.WRITE(N'shut', 0, 4), label.WRITE(NULL, 2, 0);
            SELECT t.state, t.label FROM #t AS t;
            """));

        Assert.Empty(doc.Unknowns);
        var state = Field(doc, "state");
        Assert.False(state.Nullable);
        Assert.Null(Assert.IsType<IrScalar>(state.Type).Facets?.Values);
        Assert.True(Field(doc, "label").Nullable);
    }

    [Fact]
    public void Write_makes_json_text_a_string_that_may_not_be_json()
    {
        var doc = Doc(("p", """
            CREATE PROCEDURE dbo.p AS
            SELECT i.id, (SELECT i.qty FOR JSON PATH) AS doc INTO #t FROM dbo.item AS i;
            UPDATE #t SET doc.WRITE(N'x', 0, 1);
            SELECT t.doc FROM #t AS t;
            """));

        Assert.Equal("nvarchar(max)", Assert.IsType<IrScalar>(Field(doc, "doc").Type).Sql);
    }

    [Theory]
    [InlineData("CREATE TABLE #t (id int NOT NULL, doc nvarchar(max) NULL)", "#t")]
    [InlineData("DECLARE @t TABLE (id int NOT NULL, doc nvarchar(max) NULL)", "@t")]
    public void Write_on_a_column_holding_json_built_here_makes_it_json_of_unknown_shape(string declare, string table)
    {
        var doc = Doc(("p", $"""
            CREATE PROCEDURE dbo.p AS
            {declare};
            INSERT {table} (id, doc) VALUES (1, json_object('a': 1));
            UPDATE {table} SET doc.WRITE(N'x', 0, 1);
            SELECT json_query(t.doc) AS doc FROM {table} AS t FOR JSON PATH;
            """));

        Assert.Contains("changed in place by doc.WRITE", Assert.Single(doc.Unknowns).Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("#t")]
    [InlineData("@t")]
    [InlineData("dbo.page")]
    public void A_variable_an_update_with_write_sets_may_be_set(string table)
    {
        // Every SET clause after .WRITE is read: @v was 'unset', never null, and the update may set it to a NULL label.
        var doc = Doc(("page", "CREATE TABLE dbo.page (id int NOT NULL PRIMARY KEY, label nvarchar(10) NULL, body nvarchar(max) NOT NULL);"), ("p", $"""
            CREATE PROCEDURE dbo.p AS
            CREATE TABLE #t (id int NOT NULL, label nvarchar(10) NULL, body nvarchar(max) NOT NULL);
            DECLARE @t TABLE (id int NOT NULL, label nvarchar(10) NULL, body nvarchar(max) NOT NULL);
            DECLARE @v nvarchar(10) = N'unset';
            UPDATE {table} SET body.WRITE(N'x', 0, 1), @v = label;
            SELECT @v AS v;
            """));

        Assert.True(Field(doc, "v").Nullable);
    }
}
