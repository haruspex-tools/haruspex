using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

// MERGE into the body's own tables: what each action writes joins into a temporary table's columns, as INSERT and UPDATE do,
// and a variable it sets may be set.
public partial class DmlTests
{
    /// <summary>A temporary table SELECT … INTO made of two never-null ints, merged into as <paramref name="actions"/> say.</summary>
    private static IrDocument MergedHourly(string actions, string output = "") => Doc(("p", $"""
        CREATE PROCEDURE dbo.p AS
        SELECT i.id AS hr, i.qty AS n INTO #hourly FROM dbo.item AS i WHERE i.qty > 0;
        MERGE #hourly AS t USING (SELECT i.id AS hr, i.qty AS n FROM dbo.item AS i) AS s ON t.hr = s.hr
        {actions}{output};
        SELECT h.hr, h.n FROM #hourly AS h;
        """));

    [Fact]
    public void Merge_into_a_temporary_table_keeps_its_columns_when_every_action_writes_values_of_their_kind()
    {
        // Merge_into_a_temporary_table_writes_each_actions_rows_and_may_set_a_variable.
        var doc = MergedHourly("WHEN MATCHED THEN UPDATE SET n = s.n WHEN NOT MATCHED THEN INSERT (hr, n) VALUES (s.hr, s.n)");

        Assert.Empty(doc.Unknowns);
        Assert.False(Field(doc, "hr").Nullable);
        Assert.False(Field(doc, "n").Nullable);
    }

    [Theory]
    [InlineData("WHEN MATCHED THEN UPDATE SET n = NULL")]
    [InlineData("WHEN NOT MATCHED THEN INSERT (hr) VALUES (s.hr)")]
    [InlineData("WHEN NOT MATCHED BY SOURCE THEN UPDATE SET n = NULL WHEN MATCHED THEN DELETE")]
    public void Merge_into_a_temporary_table_joins_what_each_action_writes_into_its_columns(string actions)
    {
        // A NULL set by an update, or a column an insert leaves out, joins in; a delete changes nothing a column may hold.
        var doc = MergedHourly(actions);

        Assert.Empty(doc.Unknowns);
        Assert.False(Field(doc, "hr").Nullable);
        Assert.True(Field(doc, "n").Nullable);
    }

    [Fact]
    public void Merge_into_a_declared_temporary_table_leaves_it_as_declared()
    {
        var doc = Doc(("p", """
            CREATE PROCEDURE dbo.p AS
            CREATE TABLE #hourly (hr int NOT NULL, n int NULL);
            MERGE #hourly AS t USING dbo.item AS s ON t.hr = s.id
            WHEN MATCHED THEN UPDATE SET n = s.qty WHEN NOT MATCHED THEN INSERT (hr, n) VALUES (s.id, s.qty);
            SELECT h.hr, h.n FROM #hourly AS h;
            """));

        Assert.Empty(doc.Unknowns);
        Assert.False(Field(doc, "hr").Nullable);
        Assert.Equal([IrNullRule.NullableColumn], Rules(Field(doc, "n")));
    }

    [Fact]
    public void Output_from_a_merge_into_a_temporary_table_reads_it_before_and_after()
    {
        // As for a permanent table: deleted is null for an inserted row, inserted holds what the actions wrote.
        var doc = MergedHourly(
            "WHEN MATCHED THEN UPDATE SET n = s.n WHEN NOT MATCHED THEN INSERT (hr, n) VALUES (s.hr, s.n)",
            "\nOUTPUT $action AS what, inserted.n AS now, deleted.n AS was");

        var results = Results(doc);
        Assert.Equal(2, results.Count);
        Assert.False(Field(doc, "now").Nullable);
        Assert.Equal([IrNullRule.MissingForAction], Rules(Field(doc, "was")));
        Assert.Equal(["INSERT", "UPDATE"], Assert.IsType<IrScalar>(Field(doc, "what").Type).Facets!.Values!.Items.Select(v => v.Text));
    }

    [Fact]
    public void A_variable_a_merge_sets_may_be_set_to_any_value_it_reads()
    {
        // Merge_into_a_temporary_table_writes_each_actions_rows_and_may_set_a_variable: UPDATE SET @v = … in a MERGE sets it
        // as an UPDATE's does, so the variable may now be null, whatever the target.
        var doc = Doc(("p", """
            CREATE PROCEDURE dbo.p AS
            DECLARE @v nvarchar(20) = N'x';
            MERGE dbo.item AS t USING dbo.note AS s ON t.id = s.id WHEN MATCHED THEN UPDATE SET @v = s.text, qty = 1;
            SELECT @v AS v;
            """));

        Assert.True(Field(doc, "v").Nullable);
    }
}
