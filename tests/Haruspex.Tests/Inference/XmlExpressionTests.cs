using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

/// <summary>
/// XML's expressions beyond its values (decision 0137): <c>nodes()</c> as a source, <c>query()</c>, <c>FOR XML</c> subqueries and
/// <c>OPENXML … WITH</c>. Each rule rests on an observation in the differential tests' <c>XmlExpressionObservationTests</c>,
/// named where it matters.
/// </summary>
public class XmlExpressionTests
{
    private const string Tables = """
        CREATE TABLE shop.product (
            product_id int NOT NULL PRIMARY KEY, name nvarchar(40) NOT NULL, spec xml NOT NULL, notes xml NULL);
        GO
        CREATE TABLE shop.shape (sku varchar(5) NOT NULL, size varchar(3) NULL);
        GO
        """;

    private static IrDocument Procedure(string body) =>
        InferProcedure.Infer("shop.p", ("tables.sql", Tables), ("p.sql", $"CREATE PROCEDURE shop.p AS\n{body}"));

    /// <summary>The field's kind and SQL type (<c>xml xml</c>), or <c>unknown</c> with the reason's detail.</summary>
    private static string Typed(IrDocument doc, string name, int result = 0) => Field(doc, name, result).Type switch
    {
        IrScalar scalar => $"{IrWireNames.Of(scalar.Kind)} {scalar.Sql}",
        IrUnknownType => "unknown: " + string.Join("; ", doc.Unknowns.Where(u => u.Path.EndsWith("." + name, StringComparison.Ordinal)).Select(u => u.Detail)),
        var other => other.ToString(),
    };

    // ---- nodes() ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void Nodes_gives_a_node_that_xml_s_methods_read_as_any_xml_value()
    {
        // A_nodes_column_is_never_null_and_is_read_only_by_xml_s_methods.
        var doc = Procedure("""
            SELECT n.c.value('@sku', 'varchar(20)') AS sku, n.c.exist('size') AS sized, n.c.query('size') AS size
            FROM shop.product AS p CROSS APPLY p.spec.nodes('/spec/part') AS n(c);
            """);

        Assert.Equal("string varchar(20)", Typed(doc, "sku"));
        Assert.Equal([IrNullRule.XmlValue], Rules(doc, "sku"));
        Assert.Equal("boolean bit", Typed(doc, "sized"));
        Assert.Empty(Field(doc, "sized").NullableBecause);
        Assert.Equal("xml xml", Typed(doc, "size"));
        Assert.Empty(Field(doc, "size").NullableBecause);
    }

    [Fact]
    public void Outer_apply_of_nodes_keeps_the_outer_row_with_null()
    {
        // Nodes_gives_a_row_for_each_node_and_none_for_null: OUTER APPLY null-extends a row whose path finds nothing.
        var doc = Procedure("""
            SELECT p.product_id, n.c.exist('size') AS sized, n.c.query('size') AS size
            FROM shop.product AS p OUTER APPLY p.notes.nodes('/notes/note') AS n(c);
            """);

        Assert.Equal([IrNullRule.UnmatchedLateralRow], Rules(doc, "sized"));
        Assert.Equal([IrNullRule.UnmatchedLateralRow], Rules(doc, "size"));
        Assert.Empty(Field(doc, "product_id").NullableBecause);
    }

    [Fact]
    public void Nodes_may_give_no_row_so_a_subquery_over_it_may_be_null()
    {
        // Nodes_gives_a_row_for_each_node_and_none_for_null: no row for a path that finds nothing, nor for a NULL instance.
        var doc = Procedure("""
            SELECT (SELECT TOP (1) n.c.exist('.') FROM p.spec.nodes('/spec/part') AS n(c)) AS first_part
            FROM shop.product AS p;
            """);

        Assert.Equal([IrNullRule.EmptySubquery], Rules(doc, "first_part"));
    }

    [Fact]
    public void Nodes_of_a_variable_of_a_column_named_alone_and_of_a_node_are_typed()
    {
        var doc = Procedure("""
            DECLARE @x xml = N'<r><i>1</i></r>';
            SELECT t.c.value('.', 'int') AS a FROM @x.nodes('/r/i') AS t(c);
            SELECT n.c.exist('.') AS b FROM shop.product AS p CROSS APPLY spec.nodes('/spec') AS n(c);
            SELECT u.d.query('.') AS c
            FROM shop.product AS p CROSS APPLY p.spec.nodes('/spec') AS t(c) CROSS APPLY t.c.nodes('part') AS u(d);
            """);

        Assert.Equal("number int", Typed(doc, "a", 0));
        Assert.Equal("boolean bit", Typed(doc, "b", 1));
        Assert.Equal("xml xml", Typed(doc, "c", 2));
        Assert.Empty(Field(doc, "c", 2).NullableBecause);
    }

    [Theory]
    [InlineData("SELECT n.c AS a FROM shop.product AS p CROSS APPLY p.spec.nodes('/spec') AS n(c);")]
    [InlineData("SELECT CAST(n.c AS nvarchar(max)) AS a FROM shop.product AS p CROSS APPLY p.spec.nodes('/spec') AS n(c);")]
    public void A_nodes_column_read_bare_is_unknown_as_the_server_refuses_it(string body)
    {
        // A_nodes_column_is_never_null_and_is_read_only_by_xml_s_methods: 493 and 525.
        var doc = Procedure(body);

        Assert.Contains("(493)", Typed(doc, "a"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_nodes_column_read_by_star_is_unknown_as_the_server_refuses_it()
    {
        var doc = Procedure("SELECT n.* FROM shop.product AS p CROSS APPLY p.spec.nodes('/spec') AS n(c);");

        Assert.Contains("(493)", Typed(doc, "c"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CROSS APPLY p.spec.nodes('/spec') AS n(c, d)", "(8159)")]
    [InlineData("CROSS APPLY p.spec.nodes(N'/spec' + N'') AS n(c)", "(8172)")]
    [InlineData("CROSS APPLY p.name.nodes('/spec') AS n(c)", "(9506)")]
    public void Nodes_the_server_refuses_is_unknown_with_its_reason(string apply, string error)
    {
        var doc = Procedure($"SELECT n.c.exist('.') AS a FROM shop.product AS p {apply};");

        Assert.Contains(error, Typed(doc, "a"), StringComparison.Ordinal);
        Assert.Equal(IrUnknownReason.UnsupportedSource, Assert.IsType<IrUnknownType>(Field(doc, "a").Type).Reason);
    }

    // ---- query() ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void Query_is_untyped_xml_null_exactly_when_its_instance_is()
    {
        // Query_is_untyped_xml_null_exactly_when_its_instance_is: an empty instance where the path finds nothing.
        var doc = Procedure("""
            DECLARE @x xml;
            SELECT p.spec.query('/spec/part') AS a, p.notes.query('/notes') AS b, @x.query('.') AS c FROM shop.product AS p;
            """);

        Assert.Equal("xml xml", Typed(doc, "a"));
        Assert.Empty(Field(doc, "a").NullableBecause);
        Assert.Equal([IrNullRule.NullableColumn], Rules(doc, "b"));
        Assert.Equal("xml xml", Typed(doc, "c"));
        Assert.NotEmpty(Field(doc, "c").NullableBecause);
    }

    [Fact]
    public void Query_with_a_path_that_is_no_literal_is_unknown()
    {
        var doc = Procedure("DECLARE @path nvarchar(20) = N'/spec'; SELECT p.spec.query(@path) AS a FROM shop.product AS p;");

        Assert.Contains("literal path", Typed(doc, "a"), StringComparison.Ordinal);
    }

    // ---- FOR XML subqueries ----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("FOR XML PATH(''), TYPE", "xml xml")]
    [InlineData("FOR XML PATH('')", "string nvarchar(max)")]
    [InlineData("FOR XML RAW", "string nvarchar(max)")]
    [InlineData("FOR XML RAW, TYPE", "xml xml")]
    [InlineData("FOR XML AUTO, ELEMENTS", "string nvarchar(max)")]
    [InlineData("FOR XML PATH('part'), ROOT('parts')", "string nvarchar(max)")]
    [InlineData("FOR XML PATH('part'), ROOT('parts'), TYPE", "xml xml")]
    public void A_for_xml_subquery_is_xml_with_type_and_nvarchar_max_without(string clause, string type)
    {
        // A_for_xml_subquery_is_xml_with_type_and_nvarchar_max_without_and_null_only_over_no_rows.
        var doc = Procedure($"SELECT (SELECT p.name FROM shop.product AS p {clause}) AS a;");

        Assert.Equal(type, Typed(doc, "a"));
        Assert.Equal([IrNullRule.EmptySubquery], Rules(doc, "a"));
    }

    [Fact]
    public void A_for_xml_subquery_of_exactly_one_row_is_never_null()
    {
        // A_for_xml_subquery_is_xml_with_type_and_nvarchar_max_without_and_null_only_over_no_rows: a row whose every
        // column is NULL still gives '' without TYPE, and an empty instance with it.
        var doc = Procedure("""
            SELECT (SELECT COUNT(*) AS n FROM shop.product AS p FOR XML PATH('')) AS a,
                   (SELECT NULL AS n FOR XML PATH(''), TYPE) AS b;
            """);

        Assert.Empty(Field(doc, "a").NullableBecause);
        Assert.Empty(Field(doc, "b").NullableBecause);
    }

    [Fact]
    public void The_stuff_idiom_over_for_xml_path_is_nvarchar_max_and_nullable()
    {
        // STUFF of '' (no row's name) is NULL too: STUFF's own rule.
        var doc = Procedure("SELECT STUFF((SELECT ',' + p.name FROM shop.product AS p FOR XML PATH('')), 1, 1, '') AS names;");

        Assert.Equal("string nvarchar(max)", Typed(doc, "names"));
        Assert.Contains(IrNullRule.EmptySubquery, Rules(doc, "names"));
    }

    [Fact]
    public void A_for_xml_subquery_over_a_column_not_modelled_is_unknown()
    {
        var doc = Procedure("SELECT (SELECT p.missing FROM shop.product AS p FOR XML PATH('')) AS a;");

        Assert.StartsWith("unknown: ", Typed(doc, "a"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SELECT d.names FROM (SELECT p.name FROM shop.product AS p FOR XML PATH('')) AS d (names);", "string nvarchar(max)")]
    [InlineData("WITH c (names) AS (SELECT p.name FROM shop.product AS p FOR XML PATH(''), TYPE) SELECT c.names FROM c;", "xml xml")]
    public void A_for_xml_derived_table_or_cte_is_one_row_of_what_its_query_builds(string body, string type)
    {
        // A_for_xml_derived_table_or_cte_is_one_row_of_what_the_query_builds: main typed it as nvarchar(40), never null, of
        // many rows.
        var doc = Procedure(body);

        Assert.Equal(type, Typed(doc, "names"));
        Assert.Equal([IrNullRule.EmptySubquery], Rules(doc, "names"));
        Assert.Equal(IrCardinality.One, Results(doc)[0].Cardinality);
    }

    [Fact]
    public void A_for_xml_derived_table_without_a_column_list_or_for_json_is_unknown()
    {
        var doc = Procedure("""
            SELECT d.name AS a FROM (SELECT p.name FROM shop.product AS p FOR XML PATH('')) AS d;
            SELECT d.j AS b FROM (SELECT p.name FROM shop.product AS p FOR JSON PATH) AS d (j);
            """);

        Assert.StartsWith("unknown: ", Typed(doc, "a", 0), StringComparison.Ordinal);
        Assert.StartsWith("unknown: ", Typed(doc, "b", 1), StringComparison.Ordinal);
    }

    // ---- OPENXML ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void Openxml_with_columns_are_their_declared_types_and_always_nullable()
    {
        // Openxml_with_columns_are_their_declared_types_converted_as_cast_converts_text.
        var doc = Procedure("""
            DECLARE @h int;
            EXEC sp_xml_preparedocument @h OUTPUT, N'<r><i sku="a1" size="large"/></r>';
            SELECT x.sku, x.size, x.qty, x.price, x.code, x.node
            FROM OPENXML(@h, '/r/i', 1) WITH (sku varchar(5), size varchar(3), qty int '@q', price decimal(5, 1), code char(4), node xml '.') AS x;
            EXEC sp_xml_removedocument @h;
            """);

        Assert.Single(Results(doc));
        Assert.Equal("string varchar(5)", Typed(doc, "sku"));
        Assert.Equal("string varchar(3)", Typed(doc, "size"));
        Assert.Equal("number int", Typed(doc, "qty"));
        Assert.Equal("number decimal(5,1)", Typed(doc, "price"));
        Assert.Equal("string char(4)", Typed(doc, "code"));
        Assert.Equal("xml xml", Typed(doc, "node"));
        foreach (var name in new[] { "sku", "size", "qty", "price", "code", "node" })
            Assert.Contains(IrNullRule.XmlValue, Rules(doc, name));
    }

    [Fact]
    public void Openxml_with_a_table_s_name_is_null_even_where_the_table_declares_not_null()
    {
        // Openxml_with_a_table_s_name_is_null_where_its_path_finds_nothing_whatever_the_table_declares.
        var doc = Procedure("DECLARE @h int; SELECT x.sku, x.size FROM OPENXML(@h, '/r/i', 1) WITH shop.shape AS x;");

        Assert.Equal("string varchar(5)", Typed(doc, "sku"));
        Assert.Equal([IrNullRule.XmlValue], Rules(doc, "sku"));
        Assert.Equal("p.sql:2:43", Field(doc, "sku").NullableBecause[0].At);
        Assert.Equal("string varchar(3)", Typed(doc, "size"));
    }

    [Theory]
    [InlineData("SELECT x.id AS a FROM OPENXML(@h, '/r/i') AS x;", "edge table")]
    [InlineData("SELECT x.sku AS a FROM OPENXML(@h, '/r/i', 1) WITH shape AS x;", "named with its schema")]
    public void Openxml_without_a_with_clause_it_reads_is_unknown(string select, string why)
    {
        var doc = Procedure($"DECLARE @h int; {select}");

        Assert.Contains(why, Typed(doc, "a"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("timestamp")]
    [InlineData("text")]
    [InlineData("sql_variant")]
    [InlineData("hierarchyid")]
    public void An_openxml_column_of_a_type_not_observed_is_unknown(string type)
    {
        var doc = Procedure($"DECLARE @h int; SELECT x.a FROM OPENXML(@h, '/r/i', 1) WITH (a {type}) AS x;");

        Assert.StartsWith("unknown: ", Typed(doc, "a"), StringComparison.Ordinal);
    }
}
