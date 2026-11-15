using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

/// <summary>
/// <c>SELECT *</c> and <c>alias.*</c>, expanded wherever a query stands: each column as if it were listed by name. The engine
/// rules each test leans on are observed in the differential tests' <c>SelectStarObservationTests</c>, named where they matter;
/// a view's star is in <c>ApplyViewTests.Views.cs</c>.
/// </summary>
public partial class SelectStarTests
{
    private const string Tables = """
        CREATE TABLE shop.orders (order_id int NOT NULL PRIMARY KEY, customer nvarchar(40) NOT NULL, placed_on date NOT NULL, note nvarchar(200) NULL);
        GO
        CREATE TABLE shop.order_line (line_id int NOT NULL PRIMARY KEY, order_id int NOT NULL, sku varchar(12) NOT NULL, qty int NULL);
        GO
        """;

    private static IrDocument Infer(params (string File, string Sql)[] files) =>
        InferProcedure.Infer("shop.p", [("schema.sql", "CREATE SCHEMA shop;"), ("tables.sql", Tables), .. files]);

    /// <summary>Infers <c>shop.p</c>, whose body is <paramref name="body"/>, over the standard tables and any others.</summary>
    private static IrDocument Procedure(string body, string parameters = "", string others = "") =>
        Infer(("others.sql", others.Length == 0 ? "SELECT 1;" : others), ("p.sql", $"CREATE PROCEDURE shop.p {parameters} AS\n{body}"));

    private static string[] Names(IrDocument doc, int result = 0) => [.. Fields(doc, result).Select(f => f.Name)];

    // ---- a procedure's result ------------------------------------------------------------------------------------

    [Fact]
    public void Select_star_in_a_procedure_is_its_tables_columns_in_declared_order()
    {
        var doc = Procedure("SELECT * FROM shop.orders AS o;");

        AllTyped(doc);
        Assert.Equal(["order_id", "customer", "placed_on", "note"], Names(doc));
        Assert.Empty(Rules(doc, "customer"));
        Assert.Equal([IrNullRule.NullableColumn], Rules(doc, "note"));
        Assert.Equal("shop.orders.note", Field(doc, "note").NullableBecause[0].At);
    }

    [Fact]
    public void Each_column_of_a_star_keeps_what_the_query_proves_of_it()
    {
        // As if listed by name: the WHERE proves note non-null, and the key fixed by the parameter makes one row at most.
        var doc = Procedure("SELECT * FROM shop.orders AS o WHERE o.order_id = @id AND o.note IS NOT NULL;", "@id int");

        AllTyped(doc);
        Assert.Empty(Rules(doc, "note"));
        Assert.Equal(IrCardinality.AtMostOne, Assert.Single(Results(doc)).Cardinality);
    }

    [Fact]
    public void Alias_star_expands_only_its_own_source_and_keeps_its_optional_side()
    {
        var doc = Procedure("SELECT o.customer, l.* FROM shop.orders AS o LEFT JOIN shop.order_line AS l ON l.order_id = o.order_id;");

        AllTyped(doc);
        Assert.Equal(["customer", "line_id", "order_id", "sku", "qty"], Names(doc));
        Assert.Equal([IrNullRule.UnmatchedRow], Rules(doc, "sku"));
    }

    [Fact]
    public void Star_over_two_sources_that_share_a_name_makes_the_rowset_unknown_naming_it()
    {
        // A_rowset_returns_both_columns_of_one_name: SQL Server returns both, but the mssql driver turns them into one array.
        var doc = Procedure("SELECT * FROM shop.orders AS o JOIN shop.order_line AS l ON l.order_id = o.order_id;");

        var unknown = Unknown(doc, "results[0]");
        Assert.Equal(IrUnknownReason.DuplicateColumn, unknown.Reason);
        Assert.Contains("'order_id'", unknown.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Star_over_a_table_not_in_the_project_is_unknown_for_that_reason()
    {
        var unknown = Unknown(Procedure("SELECT * FROM shop.nowhere AS n;"), "results[0]");

        Assert.Equal(IrUnknownReason.UnresolvedTable, unknown.Reason);
        Assert.Contains("shop.nowhere", unknown.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_star_without_from_is_unknown_as_sql_server_refuses_it() =>
        AssertUnknown(Procedure("SELECT *;"), "results[0]", IrUnknownReason.UnsupportedQuery);

    [Fact]
    public void A_star_qualified_by_schema_and_table_lists_that_tables_columns()
    {
        var full = Procedure("SELECT shop.orders.* FROM shop.orders JOIN shop.order_line AS l ON l.order_id = shop.orders.order_id;");
        var aliased = Procedure("SELECT o.* FROM shop.orders AS o JOIN shop.order_line AS l ON l.order_id = o.order_id;");

        AllTyped(full);
        Assert.Equal(Names(aliased), Names(full));
    }

    [Fact]
    public void A_star_qualified_by_schema_and_table_on_an_outer_join_s_optional_side_is_nullable()
    {
        // Observed: A_full_name_star_on_an_optional_side_is_null_where_nothing_matches.
        var doc = Procedure("SELECT o.customer, shop.order_line.* FROM shop.orders AS o LEFT JOIN shop.order_line ON shop.order_line.order_id = o.order_id;");

        Assert.Equal(["customer", "line_id", "order_id", "sku", "qty"], Names(doc));
        Assert.All(Fields(doc).Skip(1), f => Assert.True(f.Nullable));
        Assert.False(Fields(doc)[0].Nullable);
    }

    [Fact]
    public void A_star_qualified_by_the_full_name_of_an_aliased_table_is_unknown() =>
        // Observed: An_alias_hides_a_tables_full_name (Msg 107 for the star).
        AssertUnknown(Procedure("SELECT shop.orders.* FROM shop.orders AS o;"), "results[0]", IrUnknownReason.UnresolvedColumn);

    [Fact]
    public void A_star_with_other_columns_keeps_the_select_lists_order()
    {
        var doc = Procedure("SELECT 1 AS first, l.*, o.customer FROM shop.order_line AS l JOIN shop.orders AS o ON o.order_id = l.order_id;");

        AllTyped(doc);
        Assert.Equal(["first", "line_id", "order_id", "sku", "qty", "customer"], Names(doc));
    }

    // ---- FOR JSON ------------------------------------------------------------------------------------------------

    [Fact]
    public void Select_star_before_for_json_has_a_key_per_column()
    {
        var doc = Procedure("SELECT * FROM shop.orders WHERE order_id = @id FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;", "@id int");

        AllTyped(doc);
        var fields = Fields(doc);
        Assert.Equal(["order_id", "customer", "placed_on", "note"], fields.Select(f => f.Name));
        // FOR JSON leaves out a null key: note is omitted when null, for the column's reason.
        Assert.Equal(IrPresence.Always, fields[1].Presence);
        Assert.Equal(IrPresence.OmittedWhenNull, fields[3].Presence);
        Assert.Equal([IrNullRule.NullableColumn], Rules(fields[3]));
    }

    [Fact]
    public void Star_over_two_sources_that_share_a_name_before_for_json_is_unknown()
    {
        // For_json_refuses_two_columns_of_one_name: Msg 13601 when the procedure runs, so it never returns the object.
        var unknown = Unknown(Procedure("SELECT * FROM shop.orders AS o JOIN shop.order_line AS l ON l.order_id = o.order_id FOR JSON PATH;"), "results[0]");

        Assert.Equal(IrUnknownReason.DuplicateColumn, unknown.Reason);
        Assert.Contains("'order_id'", unknown.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Select_star_in_a_for_json_subquery_nests_every_column()
    {
        var doc = Procedure("SELECT o.order_id, (SELECT l.* FROM shop.order_line AS l WHERE l.order_id = o.order_id FOR JSON PATH) AS lines FROM shop.orders AS o;");

        AllTyped(doc);
        var lines = Field(doc, "lines").Type;
        var items = Assert.IsType<IrArray>(lines is IrJsonText { Content: var content } ? content : lines).Items;
        Assert.Equal(["line_id", "order_id", "sku", "qty"], Fields(doc, items).Select(f => f.Name));
    }

    // ---- hidden columns ------------------------------------------------------------------------------------------

    [Fact]
    public void Select_star_leaves_out_a_temporal_tables_hidden_period_columns()
    {
        // A_star_leaves_out_hidden_period_columns.
        var doc = Procedure(
            "SELECT * FROM shop.price AS p;",
            others: "CREATE TABLE shop.price (sku varchar(12) NOT NULL PRIMARY KEY, amount decimal(9,2) NOT NULL, valid_from datetime2 GENERATED ALWAYS AS ROW START HIDDEN NOT NULL, valid_to datetime2 GENERATED ALWAYS AS ROW END HIDDEN NOT NULL, PERIOD FOR SYSTEM_TIME (valid_from, valid_to));");

        AllTyped(doc);
        Assert.Equal(["sku", "amount"], Names(doc));
    }

    [Fact]
    public void Select_star_keeps_a_period_column_that_is_not_hidden()
    {
        var doc = Procedure(
            "SELECT * FROM shop.price AS p;",
            others: "CREATE TABLE shop.price (sku varchar(12) NOT NULL PRIMARY KEY, valid_from datetime2 GENERATED ALWAYS AS ROW START HIDDEN NOT NULL, valid_to datetime2 GENERATED ALWAYS AS ROW END NOT NULL, PERIOD FOR SYSTEM_TIME (valid_from, valid_to));");

        Assert.Equal(["sku", "valid_to"], Names(doc));
    }

    [Fact]
    public void Select_star_over_a_column_set_gives_the_set_in_place_of_its_sparse_columns()
    {
        // A_column_set_stands_for_its_sparse_columns_in_star: the xml column set is the one column for all of them.
        var doc = Procedure(
            "SELECT * FROM shop.parcel AS p;",
            others: "CREATE TABLE shop.parcel (parcel_id int NOT NULL, weight int SPARSE NULL, label nvarchar(20) NULL, fragile bit SPARSE NULL, extras xml COLUMN_SET FOR ALL_SPARSE_COLUMNS);");

        Assert.Equal(["parcel_id", "label", "extras"], Names(doc));
    }

    [Fact]
    public void Select_star_keeps_sparse_columns_when_the_table_has_no_column_set()
    {
        var doc = Procedure(
            "SELECT * FROM shop.parcel AS p;",
            others: "CREATE TABLE shop.parcel (parcel_id int NOT NULL, weight int SPARSE NULL);");

        AllTyped(doc);
        Assert.Equal(["parcel_id", "weight"], Names(doc));
    }

    [Theory]
    [InlineData("CREATE TABLE shop.depot (depot_id int NOT NULL) AS NODE;")]
    [InlineData("CREATE TABLE shop.route (km int NULL) AS EDGE;")]
    public void Select_star_over_a_graph_table_is_unknown_since_its_pseudo_columns_are_named_by_the_server(string table)
    {
        // A_graph_tables_star_adds_pseudo_columns_named_by_the_server: $node_id_ and a generated suffix.
        var unknown = Unknown(Procedure($"SELECT * FROM shop.{(table.Contains("depot", StringComparison.Ordinal) ? "depot" : "route")} AS g;", others: table), "results[0]");

        Assert.Equal(IrUnknownReason.UnsupportedQuery, unknown.Reason);
        Assert.Contains("graph", unknown.Detail, StringComparison.Ordinal);
    }
}
