using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

// SELECT * over a body's own relations, and inside them: CTEs, derived tables, set operations, temporary tables and table
// variables, SELECT … INTO and INSERT … SELECT.
public partial class SelectStarTests
{
    [Fact]
    public void Select_star_in_a_cte_and_over_it_is_every_column()
    {
        var doc = Procedure("WITH open_orders AS (SELECT * FROM shop.orders AS o WHERE o.note IS NOT NULL) SELECT * FROM open_orders;");

        AllTyped(doc);
        Assert.Equal(["order_id", "customer", "placed_on", "note"], Names(doc));
        Assert.Empty(Rules(doc, "note"));
    }

    [Fact]
    public void Select_star_in_a_derived_table_and_over_it_is_every_column()
    {
        var doc = Procedure("SELECT d.* FROM (SELECT * FROM shop.order_line AS l) AS d;");

        AllTyped(doc);
        Assert.Equal(["line_id", "order_id", "sku", "qty"], Names(doc));
    }

    [Fact]
    public void A_derived_table_whose_star_repeats_a_name_is_unknown()
    {
        // A_derived_table_or_cte_refuses_two_columns_of_one_name: Msg 8156, so the procedure never returns it.
        var doc = Procedure("SELECT d.sku FROM (SELECT * FROM shop.orders AS o JOIN shop.order_line AS l ON l.order_id = o.order_id) AS d;");

        Assert.NotEmpty(doc.Unknowns);
    }

    [Fact]
    public void Select_star_in_a_set_operation_is_expanded_in_each_query()
    {
        var doc = Procedure("SELECT * FROM shop.order_line AS l WHERE l.qty > 0 UNION ALL SELECT * FROM shop.order_line AS l WHERE l.qty IS NULL;");

        AllTyped(doc);
        Assert.Equal(["line_id", "order_id", "sku", "qty"], Names(doc));
        Assert.NotEmpty(Rules(doc, "qty"));
    }

    [Fact]
    public void Select_star_over_a_temporary_table_is_its_columns_at_that_statement()
    {
        // A_temporary_tables_star_is_its_columns_when_the_statement_runs: the ALTER's column is in the second result only.
        var doc = Procedure("""
            SELECT o.order_id, o.note INTO #picked FROM shop.orders AS o;
            SELECT * FROM #picked;
            ALTER TABLE #picked ADD packed bit NULL;
            SELECT * FROM #picked;
            """);

        Assert.Equal(["order_id", "note"], Names(doc, 0));
        Assert.Equal([IrNullRule.NullableColumn], Rules(Field(doc, "note", 0)));
        Assert.Equal(["order_id", "note", "packed"], Names(doc, 1));
    }

    [Fact]
    public void Select_star_into_a_temporary_table_copies_every_column()
    {
        var doc = Procedure("""
            SELECT * INTO #copy FROM shop.orders AS o WHERE o.note IS NOT NULL;
            SELECT c.note, c.customer FROM #copy AS c;
            """);

        AllTyped(doc);
        Assert.Empty(Rules(doc, "note"));
    }

    [Fact]
    public void Select_star_into_from_two_sources_sharing_a_name_is_refused_as_sql_server_does()
    {
        // Select_into_refuses_two_columns_of_one_name: Msg 2705.
        var doc = Procedure("""
            SELECT * INTO #both FROM shop.orders AS o JOIN shop.order_line AS l ON l.order_id = o.order_id;
            SELECT b.sku FROM #both AS b;
            """);

        Assert.NotEmpty(doc.Unknowns);
    }

    [Fact]
    public void Select_star_over_a_callers_temporary_table_is_unknown()
    {
        var unknown = Unknown(Procedure("SELECT * FROM #callers;"), "results[0]");

        Assert.Equal(IrUnknownReason.CallerTempTable, unknown.Reason);
    }

    [Fact]
    public void Select_star_over_a_table_variable_is_its_declared_columns()
    {
        var doc = Procedure("""
            DECLARE @picked TABLE (order_id int NOT NULL, note nvarchar(200) NULL);
            INSERT INTO @picked SELECT o.order_id, o.note FROM shop.orders AS o;
            SELECT * FROM @picked;
            """);

        AllTyped(doc);
        Assert.Equal(["order_id", "note"], Names(doc));
        Assert.Empty(Rules(doc, "order_id"));
    }

    [Fact]
    public void Select_star_over_a_table_variable_with_a_column_set_gives_the_set_in_place_of_its_sparse_columns()
    {
        // A_column_set_stands_for_its_sparse_columns_in_star, observed for a table variable too.
        var doc = Procedure("""
            DECLARE @parcels TABLE (parcel_id int NOT NULL, weight int SPARSE NULL, extras xml COLUMN_SET FOR ALL_SPARSE_COLUMNS);
            SELECT * FROM @parcels;
            """);

        Assert.Equal(["parcel_id", "extras"], Names(doc));
    }

    [Theory]
    [InlineData("CREATE TABLE #parcels (parcel_id int NOT NULL, weight int SPARSE NULL, extras xml COLUMN_SET FOR ALL_SPARSE_COLUMNS);")]
    [InlineData("CREATE TABLE #parcels (parcel_id int NOT NULL); ALTER TABLE #parcels ADD extras xml COLUMN_SET FOR ALL_SPARSE_COLUMNS;")]
    public void A_temporary_table_with_a_column_set_is_not_modelled(string create)
    {
        // Its star would give the set in place of its sparse columns, which the columns binding is given can't say.
        var unknown = Unknown(Procedure($"{create}\nSELECT * FROM #parcels;"), "results[0]");

        Assert.Equal(IrUnknownReason.UnsupportedSource, unknown.Reason);
        Assert.Contains("column set", unknown.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Insert_select_star_into_a_temporary_table_writes_every_column()
    {
        // A column SELECT … INTO made joins with what the star's column gives, as with an insert of a listed query.
        var doc = Procedure("""
            SELECT l.* INTO #lines FROM shop.order_line AS l WHERE l.qty IS NOT NULL;
            INSERT INTO #lines SELECT * FROM shop.order_line AS l WHERE l.qty > 0;
            SELECT x.qty FROM #lines AS x;
            """);

        AllTyped(doc);
        Assert.Empty(Rules(doc, "qty"));
    }

    [Fact]
    public void A_scalar_subquery_of_alias_star_over_one_column_is_its_value()
    {
        var doc = Procedure("SELECT (SELECT TOP (1) d.* FROM (SELECT l.sku FROM shop.order_line AS l) AS d) AS sku;");

        AllTyped(doc);
    }

    [Fact]
    public void Exists_over_a_star_is_unchanged()
    {
        var doc = Procedure("SELECT o.order_id FROM shop.orders AS o WHERE EXISTS (SELECT * FROM shop.order_line AS l WHERE l.order_id = o.order_id);");

        AllTyped(doc);
    }
}
