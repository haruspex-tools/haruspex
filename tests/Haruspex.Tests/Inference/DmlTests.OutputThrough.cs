using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

// A write's OUTPUT through a CTE or a derived table of one table: the queue-claim idiom. Observed in DmlObservationTests'
// OutputThrough part.
public partial class DmlTests
{
    [Fact]
    public void Output_through_a_cte_of_one_table_is_typed_from_the_tables_columns()
    {
        // Observed: Output_through_a_cte_or_derived_table_is_the_columns_it_names_typed_as_the_table_declares_them. The CTE's
        // WHERE proves label is not null in its rows before the write, which the write then sets to null: inserted.label is the
        // column after the write, nullable as declared, not the CTE's projection.
        var doc = Doc(("p", """
            CREATE PROCEDURE dbo.p AS
            WITH next_item AS (SELECT TOP (1) * FROM dbo.item WHERE label IS NOT NULL ORDER BY id)
            UPDATE next_item SET label = NULL, qty = qty + 1 OUTPUT inserted.id, inserted.label, inserted.qty, deleted.label AS was;
            """));

        Assert.Equal(["id", "label", "qty", "was"], Names(doc, Assert.Single(Results(doc)).Type));
        Assert.False(Field(doc, "id").Nullable);
        Assert.Equal("int", Assert.IsType<IrScalar>(Field(doc, "id").Type).Sql);
        Assert.Equal([IrNullRule.NullableColumn], Rules(Field(doc, "label")));
        Assert.Equal("nvarchar(10)", Assert.IsType<IrScalar>(Field(doc, "label").Type).Sql);
        Assert.False(Field(doc, "qty").Nullable);
        Assert.IsType<IrScalar>(Field(doc, "was").Type);
    }

    [Fact]
    public void Output_through_a_derived_table_of_one_table_lists_its_columns_for_a_star()
    {
        var doc = Doc(("p", """
            CREATE PROCEDURE dbo.p AS
            UPDATE p SET label = N'taken' OUTPUT inserted.* FROM (SELECT TOP (1) * FROM dbo.item WHERE label IS NULL ORDER BY id) AS p;
            """));

        var result = Assert.Single(Results(doc));
        Assert.Equal(["id", "label", "qty"], Names(doc, result.Type));
        Assert.False(Field(doc, "id").Nullable);
        Assert.True(Field(doc, "label").Nullable);
        Assert.False(Field(doc, "qty").Nullable);
        Assert.Equal(IrCardinality.AtMostOne, result.Cardinality);
    }

    [Theory]
    [InlineData("WITH c (n, tag) AS (SELECT id, label FROM dbo.item) UPDATE c SET tag = N'x' OUTPUT inserted.*, deleted.tag AS was;")]
    [InlineData("WITH c AS (SELECT i.id AS n, (i.label) AS tag FROM dbo.item AS i) UPDATE c SET tag = N'x' OUTPUT inserted.*, deleted.tag AS was;")]
    [InlineData("UPDATE c SET tag = N'x' OUTPUT inserted.*, deleted.tag AS was FROM (SELECT * FROM dbo.item) AS c (n, tag, amount) WHERE amount > 0;")]
    [InlineData("WITH a (n, tag) AS (SELECT id, label FROM dbo.item), b AS (SELECT a.* FROM a) UPDATE b SET tag = N'x' OUTPUT inserted.n, inserted.tag, deleted.tag AS was;")]
    [InlineData("UPDATE c SET tag = N'x' OUTPUT inserted.n, inserted.tag, deleted.tag AS was FROM (SELECT d.id AS n, d.label AS tag FROM (SELECT * FROM dbo.item) AS d) AS c;")]
    public void Output_through_a_cte_or_derived_table_names_its_columns_as_the_source_does(string write)
    {
        // Observed: the server names inserted's columns as the CTE or derived table does, and a star lists only those.
        var doc = Doc(("p", $"CREATE PROCEDURE dbo.p AS {write}"));

        var names = Names(doc, Assert.Single(Results(doc)).Type);
        Assert.Equal(["n", "tag", "was"], names.Where(n => n != "amount"));
        Assert.False(Field(doc, "n").Nullable);
        Assert.Equal("int", Assert.IsType<IrScalar>(Field(doc, "n").Type).Sql);
        Assert.Equal("nvarchar(10)", Assert.IsType<IrScalar>(Field(doc, "tag").Type).Sql);
        Assert.Equal([IrNullRule.NullableColumn], Rules(Field(doc, "was")));
    }

    [Fact]
    public void Output_through_a_cte_of_a_table_column_it_does_not_name_is_unknown()
    {
        // Observed: inserted.qty through a CTE that leaves qty out is refused (207).
        var doc = Doc(("p", "CREATE PROCEDURE dbo.p AS WITH c AS (SELECT id, label FROM dbo.item) UPDATE c SET label = N'x' OUTPUT inserted.qty;"));

        Assert.IsType<IrUnknownType>(Field(doc, "qty").Type);
    }

    [Theory]
    [InlineData("DELETE c OUTPUT deleted.id, deleted.label", "WITH c AS (SELECT TOP (1) * FROM dbo.item ORDER BY id)")]
    [InlineData("MERGE c USING (VALUES (1)) AS s (id) ON c.id = s.id WHEN MATCHED THEN UPDATE SET label = N'm' WHEN NOT MATCHED THEN INSERT (id) VALUES (s.id) OUTPUT inserted.id, deleted.label", "WITH c AS (SELECT id, label FROM dbo.item)")]
    [InlineData("INSERT INTO c (id) OUTPUT inserted.id, inserted.label VALUES (1)", "WITH c AS (SELECT id, label FROM dbo.item)")]
    public void Every_write_through_a_cte_of_one_table_is_typed_from_the_table(string write, string with)
    {
        // Observed: Output_through_a_cte_or_derived_table_is_the_columns_it_names_typed_as_the_table_declares_them.
        var doc = Doc(("p", $"CREATE PROCEDURE dbo.p AS {with} {write};"));

        Assert.IsType<IrScalar>(Field(doc, "id").Type);
        Assert.IsType<IrScalar>(Field(doc, "label").Type);
        Assert.True(Field(doc, "label").Nullable);
    }

    [Theory]
    [InlineData("WITH c AS (SELECT i.id, i.label FROM dbo.item AS i JOIN dbo.note AS n ON n.id = i.id) UPDATE c SET label = N'x' OUTPUT inserted.label;", "joins")]
    [InlineData("WITH c AS (SELECT i.id, i.label FROM dbo.item AS i CROSS APPLY (SELECT 1 AS one) AS k) UPDATE c SET label = N'x' OUTPUT inserted.label;", "joins")]
    [InlineData("WITH c AS (SELECT i.id, i.label FROM dbo.item AS i, dbo.note AS n) UPDATE c SET label = N'x' OUTPUT inserted.label;", "more than one source")]
    [InlineData("UPDATE p SET label = N'x' OUTPUT inserted.label FROM (SELECT i.id, i.label FROM dbo.item AS i JOIN dbo.item AS j ON j.id = i.id) AS p;", "joins")]
    [InlineData("WITH c AS (SELECT DISTINCT label FROM dbo.item) UPDATE c SET label = N'x' OUTPUT inserted.label;", "DISTINCT")]
    [InlineData("WITH c AS (SELECT label FROM dbo.item GROUP BY label) UPDATE c SET label = N'x' OUTPUT inserted.label;", "GROUP BY")]
    [InlineData("WITH c AS (SELECT id, label FROM dbo.item UNION ALL SELECT id, text FROM dbo.note) UPDATE c SET label = N'x' OUTPUT inserted.label;", "set operation")]
    [InlineData("WITH c AS (SELECT id, label, COUNT(*) OVER () AS k FROM dbo.item) UPDATE c SET label = N'x' OUTPUT inserted.label;", "computed")]
    [InlineData("WITH c AS (SELECT id, CAST(label AS nvarchar(50)) AS label FROM dbo.item) UPDATE c SET id = 1 OUTPUT inserted.label;", "computed")]
    [InlineData("WITH c AS (SELECT id, label COLLATE Latin1_General_BIN2 AS label FROM dbo.item) UPDATE c SET label = N'x' OUTPUT inserted.label;", "computed")]
    [InlineData("WITH c AS (SELECT id, label FROM dbo.item_view) UPDATE c SET label = N'x' OUTPUT inserted.label;", "not one table of the project")]
    [InlineData("WITH c AS (SELECT id, label FROM dbo.item TABLESAMPLE (10 PERCENT)) UPDATE c SET label = N'x' OUTPUT inserted.label;", "TABLESAMPLE")]
    [InlineData("DECLARE @t table (id int, label nvarchar(10)); WITH c AS (SELECT id, label FROM @t) UPDATE c SET label = N'x' OUTPUT inserted.label;", "table variable")]
    [InlineData("CREATE TABLE #t (id int, label nvarchar(10)); WITH c AS (SELECT id, label FROM #t) UPDATE c SET label = N'x' OUTPUT inserted.label;", "temporary table")]
    [InlineData("WITH c AS (SELECT * FROM (VALUES (1, N'a')) AS v (id, label)) UPDATE c SET label = N'x' OUTPUT inserted.label;", "VALUES")]
    public void Output_through_a_source_that_is_not_one_table_is_unknown_with_why(string write, string why)
    {
        // Observed: Writes_through_a_cte_of_a_join_an_aggregate_distinct_or_a_set_operation_are_refused_or_reach_another_table.
        var doc = Doc(
            ("view", "CREATE VIEW dbo.item_view AS SELECT id, label FROM dbo.item;"),
            ("p", $"CREATE PROCEDURE dbo.p AS {write}"));

        Assert.IsType<IrUnknownType>(Field(doc, "label").Type);
        Assert.Contains(doc.Unknowns, u => u.Object == "dbo.p" && u.Detail.Contains(why, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("WITH c AS (SELECT TOP (1) * FROM dbo.item ORDER BY id) UPDATE c SET qty = 0 OUTPUT inserted.id;", IrCardinality.AtMostOne)]
    [InlineData("WITH c AS (SELECT TOP 1 * FROM dbo.item ORDER BY id) DELETE c OUTPUT deleted.id;", IrCardinality.AtMostOne)]
    [InlineData("WITH c AS (SELECT * FROM dbo.item ORDER BY id OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY) UPDATE c SET qty = 0 OUTPUT inserted.id;", IrCardinality.AtMostOne)]
    [InlineData("WITH a AS (SELECT TOP (1) * FROM dbo.item ORDER BY id), b AS (SELECT * FROM a) UPDATE b SET qty = 0 OUTPUT inserted.id;", IrCardinality.AtMostOne)]
    [InlineData("UPDATE p SET qty = 0 OUTPUT inserted.id, n.text FROM (SELECT TOP (1) * FROM dbo.item ORDER BY id) AS p JOIN dbo.note AS n ON n.id = p.id;", IrCardinality.AtMostOne)]
    [InlineData("UPDATE TOP (1) dbo.item SET qty = 0 OUTPUT inserted.id;", IrCardinality.AtMostOne)]
    [InlineData("DELETE TOP (1) FROM dbo.item OUTPUT deleted.id;", IrCardinality.AtMostOne)]
    [InlineData("INSERT TOP (1) INTO dbo.item (id) OUTPUT inserted.id SELECT id FROM dbo.note;", IrCardinality.AtMostOne)]
    [InlineData("MERGE TOP (1) dbo.item AS t USING dbo.note AS s ON t.id = s.id WHEN MATCHED THEN DELETE WHEN NOT MATCHED THEN INSERT (id) VALUES (s.id) OUTPUT inserted.id;", IrCardinality.AtMostOne)]
    [InlineData("WITH c AS (SELECT TOP (1) id, label FROM dbo.item ORDER BY id) INSERT INTO c (id) OUTPUT inserted.id SELECT id FROM dbo.note;", IrCardinality.Many)]
    [InlineData("WITH c AS (SELECT TOP (1) id, label FROM dbo.item ORDER BY id) MERGE c USING dbo.note AS s ON c.id = s.id WHEN MATCHED THEN DELETE WHEN NOT MATCHED THEN INSERT (id) VALUES (s.id) OUTPUT inserted.id;", IrCardinality.Many)]
    [InlineData("WITH c AS (SELECT TOP (2) * FROM dbo.item ORDER BY id) UPDATE c SET qty = 0 OUTPUT inserted.id;", IrCardinality.Many)]
    [InlineData("WITH c AS (SELECT TOP (1) WITH TIES * FROM dbo.item ORDER BY qty) UPDATE c SET qty = 0 OUTPUT inserted.id;", IrCardinality.Many)]
    [InlineData("WITH c AS (SELECT TOP (1) PERCENT * FROM dbo.item ORDER BY id) UPDATE c SET qty = 0 OUTPUT inserted.id;", IrCardinality.Many)]
    [InlineData("WITH c AS (SELECT TOP (1) i.* FROM dbo.item AS i JOIN dbo.note AS n ON n.id = i.id ORDER BY i.id) UPDATE c SET qty = 0 OUTPUT inserted.id;", IrCardinality.Many)]
    public void Top_1_makes_the_output_of_an_update_or_delete_through_its_source_at_most_one_row(string write, IrCardinality rows)
    {
        // Observed: Top_1_caps_the_output_of_a_write_and_of_an_update_or_delete_through_a_cte_but_not_an_insert_or_merge_through_it.
        // A join below the TOP (the last row) is not one table, so nothing about the write is claimed.
        var doc = Doc(("p", $"CREATE PROCEDURE dbo.p AS {write}"));

        Assert.Equal(rows, Assert.Single(Results(doc)).Cardinality);
    }
}
