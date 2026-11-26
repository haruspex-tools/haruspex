using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

public partial class DmlTests
{
    [Fact]
    public void Output_without_into_returns_the_rows_written()
    {
        // Observed: Output_without_into_returns_a_result_set_even_when_no_row_is_affected,
        // Output_columns_are_the_values_converted_to_the_target_columns.
        var doc = Doc(("p", """
            CREATE PROCEDURE dbo.p AS
            INSERT INTO dbo.item (id) OUTPUT inserted.id, inserted.label, inserted.qty AS amount VALUES (1);
            UPDATE dbo.item SET label = NULL OUTPUT deleted.label AS was, inserted.label AS now WHERE id = 1;
            DELETE FROM dbo.note OUTPUT deleted.id;
            """));

        var results = Results(doc);
        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.Equal(IrCardinality.Many, r.Cardinality));
        Assert.All(results, r => Assert.False(r.MayBeAbsent));
        Assert.Equal(["id", "label", "amount"], Names(doc, results[0].Type));
        Assert.False(Field(doc, "id").Nullable);
        Assert.False(Field(doc, "amount").Nullable);
        Assert.True(Field(doc, "label").Nullable);
        Assert.Equal(["was", "now"], Names(doc, results[1].Type));
        Assert.True(Field(doc, "was", 1).Nullable);
        Assert.False(Field(doc, "id", 2).Nullable);
    }

    [Fact]
    public void Output_star_lists_the_targets_columns()
    {
        var doc = Doc(("p", "CREATE PROCEDURE dbo.p AS DELETE FROM dbo.item OUTPUT deleted.*;"));

        Assert.Equal(["id", "label", "qty"], Names(doc, Assert.Single(Results(doc)).Type));
    }

    [Fact]
    public void Merge_output_columns_are_null_for_the_actions_that_have_no_value()
    {
        // Observed: Merge_output_has_null_deleted_columns_for_inserted_rows_null_inserted_columns_for_deleted_rows_and_null_source_columns_when_not_matched_by_source.
        var doc = Doc(("p", """
            CREATE PROCEDURE dbo.p AS
            MERGE dbo.item AS t USING dbo.note AS s ON t.id = s.id
            WHEN MATCHED THEN UPDATE SET label = s.text
            WHEN NOT MATCHED BY TARGET THEN INSERT (id) VALUES (s.id)
            WHEN NOT MATCHED BY SOURCE THEN DELETE
            OUTPUT $action, inserted.id AS new_id, deleted.id AS old_id, s.id AS source_id;
            MERGE dbo.item AS t USING dbo.note AS s ON t.id = s.id
            WHEN MATCHED THEN UPDATE SET label = s.text
            OUTPUT $action AS what, inserted.id AS new_id, deleted.id AS old_id, s.id AS source_id;
            """));

        Assert.Equal(["$action", "new_id", "old_id", "source_id"], Names(doc, Results(doc)[0].Type));
        Assert.Equal([IrNullRule.MissingForAction], Rules(Field(doc, "new_id")));
        Assert.Equal([IrNullRule.MissingForAction], Rules(Field(doc, "old_id")));
        Assert.Equal([IrNullRule.MissingForAction], Rules(Field(doc, "source_id")));
        var action = Assert.IsType<IrScalar>(Field(doc, "$action").Type);
        Assert.False(Field(doc, "$action").Nullable);
        Assert.Equal(["DELETE", "INSERT", "UPDATE"], action.Facets!.Values!.Items.Select(v => v.Text));
        Assert.Equal(IrValueMatch.Exact, action.Facets.Values.Match);

        // Only updates: nothing is null, and the action is one value.
        Assert.False(Field(doc, "new_id", 1).Nullable);
        Assert.False(Field(doc, "old_id", 1).Nullable);
        Assert.False(Field(doc, "source_id", 1).Nullable);
        Assert.Equal(["UPDATE"], Assert.IsType<IrScalar>(Field(doc, "what", 1).Type).Facets!.Values!.Items.Select(v => v.Text));
    }

    [Fact]
    public void Every_source_of_a_joined_using_is_null_for_rows_not_matched_by_source()
    {
        // USING a JOIN b ON … is a source of several tables, each null-extended for those rows.
        var doc = Doc(("p", """
            CREATE PROCEDURE dbo.p AS
            MERGE dbo.item AS t USING dbo.note AS s JOIN dbo.error_log AS e ON e.id = s.id ON t.id = s.id
            WHEN MATCHED THEN UPDATE SET label = N'm'
            WHEN NOT MATCHED BY SOURCE THEN DELETE
            OUTPUT s.id AS note_id, e.at;
            """));

        Assert.Equal([IrNullRule.MissingForAction], Rules(Field(doc, "note_id")));
        Assert.Equal([IrNullRule.MissingForAction], Rules(Field(doc, "at")));
    }

    [Theory]
    [InlineData("UPDATE i SET qty = 0 OUTPUT inserted.label AS now, deleted.qty AS was FROM dbo.item AS i JOIN dbo.note AS n ON n.id = i.id;")]
    [InlineData("DELETE i OUTPUT deleted.label AS now, deleted.qty AS was FROM dbo.item AS i JOIN dbo.note AS n ON n.id = i.id;")]
    public void Output_of_a_write_to_an_alias_in_from_is_typed_from_its_table(string dml)
    {
        var doc = Doc(("p", $"CREATE PROCEDURE dbo.p AS {dml}"));

        Assert.Equal([IrNullRule.NullableColumn], Rules(Field(doc, "now")));
        Assert.False(Field(doc, "was").Nullable);
        Assert.IsType<IrScalar>(Field(doc, "was").Type);
    }

    [Fact]
    public void Output_of_the_from_sources_is_refined_by_the_where()
    {
        var doc = Doc(("p", "CREATE PROCEDURE dbo.p AS UPDATE i SET qty = 0 OUTPUT n.text FROM dbo.item AS i JOIN dbo.note AS n ON n.id = i.id WHERE n.text IS NOT NULL;"));

        Assert.False(Field(doc, "text").Nullable);
    }

    [Fact]
    public void Output_reading_the_target_by_its_own_name_is_unknown()
    {
        // Whether it is the row before or after the update is not observed, and a filter about it may not hold after.
        var doc = Doc(("p", "CREATE PROCEDURE dbo.p AS UPDATE i SET label = NULL OUTPUT i.label FROM dbo.item AS i WHERE i.label IS NOT NULL;"));

        Assert.IsType<IrUnknownType>(Field(doc, "label").Type);
    }

    [Theory]
    [InlineData("UPDATE dbo.item SET label = NULL OUTPUT dbo.item.label FROM dbo.item JOIN dbo.note AS n ON n.id = dbo.item.id WHERE dbo.item.label IS NOT NULL;")]
    [InlineData("UPDATE dbo.item SET label = NULL OUTPUT item.label FROM dbo.item JOIN dbo.note AS n ON n.id = dbo.item.id WHERE dbo.item.label IS NOT NULL;")]
    public void Output_reading_the_target_by_its_full_name_through_from_is_unknown(string statement)
    {
        // Observed: Output_cannot_name_the_target_by_its_table_name (4104), even when FROM lists it without an alias.
        var doc = Doc(("p", $"CREATE PROCEDURE dbo.p AS {statement}"));

        Assert.IsType<IrUnknownType>(Field(doc, "label").Type);
    }

    [Fact]
    public void Output_on_a_table_with_a_trigger_that_returns_nothing_is_the_output()
    {
        // Observed: Output_without_into_fails_on_a_table_with_an_enabled_trigger_and_works_when_it_is_disabled.
        var doc = Doc(
            ("trigger", "CREATE TRIGGER dbo.item_quiet ON dbo.item AFTER INSERT AS DECLARE @n int = 1;"),
            ("p", "CREATE PROCEDURE dbo.p AS INSERT INTO dbo.item (id) OUTPUT inserted.id VALUES (1);"));

        Assert.Equal(["id"], Names(doc, Assert.Single(Results(doc)).Type));
    }

    [Fact]
    public void Output_into_returns_nothing_and_writes_its_temporary_table()
    {
        // Observed: Output_into_returns_nothing_and_writes_its_rows; Output_into_a_table_with_an_enabled_trigger_fails, so the
        // OUTPUT target's own triggers are not consulted.
        var doc = Doc(
            ("trigger", "CREATE TRIGGER dbo.note_loud ON dbo.note AFTER INSERT AS SELECT N'note' AS what;"),
            ("p", """
                CREATE PROCEDURE dbo.p AS
                DECLARE @one int = 1;
                SELECT @one AS id, N'x' AS label INTO #moved;
                DECLARE @t table (id int NOT NULL, label nvarchar(10) NULL);
                UPDATE dbo.item SET qty = 0 OUTPUT inserted.id, deleted.label INTO #moved (id, label);
                UPDATE dbo.item SET qty = 0 OUTPUT inserted.id, deleted.label INTO @t;
                UPDATE dbo.item SET qty = 0 OUTPUT inserted.id, deleted.label INTO dbo.note (id, text);
                SELECT m.label FROM #moved AS m;
                """));

        var result = Assert.Single(Results(doc));
        Assert.Contains(IrNullRule.NullableColumn, Rules(Field(doc, "label")));
        Assert.Equal(["label"], Names(doc, result.Type));
    }

    [Fact]
    public void Output_from_a_temporary_table_reads_it_before_and_after()
    {
        var doc = Doc(("p", """
            CREATE PROCEDURE dbo.p AS
            DECLARE @one int = 1;
            SELECT @one AS n INTO #t;
            UPDATE #t SET n = NULL OUTPUT deleted.n AS was, inserted.n AS now;
            """));

        // deleted is the table before the update, whose column held only the variable's value; inserted, after it.
        Assert.True(Field(doc, "now").Nullable);
        Assert.False(Field(doc, "was").Nullable);
    }
}
