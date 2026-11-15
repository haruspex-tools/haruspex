using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

public partial class SetJoinTests
{
    [Fact]
    public void A_set_operation_is_named_by_its_first_query_and_typed_by_precedence_over_all()
    {
        // A_set_operations_columns_are_named_by_its_first_query_and_typed_by_precedence_over_every_query.
        var doc = Procedure("""
            SELECT t.x AS a, 'x' AS s, t.amount AS d, t.code AS c, t.tiny AS n FROM dbo.t AS t
            UNION ALL SELECT t.big AS other, N'longer', CAST(1 AS decimal(9,1)), 'ab', 300 FROM dbo.t AS t;
            """);
        AllTyped(doc);
        Assert.Equal("bigint", Sql(doc, "a"));
        Assert.Equal("nvarchar(6)", Sql(doc, "s"));
        Assert.Equal("decimal(10,2)", Sql(doc, "d"));
        Assert.Equal("varchar(4)", Sql(doc, "c"));
        Assert.Equal("int", Sql(doc, "n"));
        Assert.DoesNotContain(Fields(doc, Results(doc)[0].Type), f => f.Name == "other");
    }

    [Fact]
    public void A_chain_is_typed_over_every_query_at_once()
    {
        // A_chain_of_set_operations_is_typed_over_all_its_queries_at_once_so_an_integer_literal_keeps_its_digits.
        Assert.Equal("numeric(2,1)", Sql(Procedure("SELECT 1 AS a UNION ALL SELECT 2 UNION ALL SELECT 2.5;"), "a"));
        Assert.Equal("numeric(11,1)", Sql(Procedure("SELECT 1 AS a UNION ALL SELECT t.x FROM dbo.t AS t UNION ALL SELECT 2.5;"), "a"));
        Assert.Equal("numeric(11,1)", Sql(Procedure("SELECT 2.5 AS a UNION ALL (SELECT 1 UNION ALL SELECT t.x FROM dbo.t AS t);"), "a"));
    }

    [Fact]
    public void A_null_literal_adds_nothing_to_the_type_but_makes_the_column_nullable()
    {
        // A_null_literal_adds_nothing_to_a_set_operations_type_and_alone_is_int.
        var doc = Procedure("SELECT NULL AS a, t.id AS b FROM dbo.t AS t UNION ALL SELECT t.label, NULL FROM dbo.t AS t;");
        Assert.Equal("varchar(8) null-literal", Typed(doc, "a"));
        Assert.Equal("int null-literal", Typed(doc, "b"));
        Assert.IsType<IrUnknownType>(Field(Procedure("SELECT NULL AS a UNION SELECT NULL;"), "a").Type);
    }

    [Fact]
    public void A_column_of_a_type_not_modelled_or_unknown_in_any_query_is_unknown()
    {
        Assert.IsType<IrUnknownType>(Field(Procedure("SELECT t.id AS a FROM dbo.t AS t UNION ALL SELECT NEWID() FROM dbo.t AS t;"), "a").Type);
        Assert.IsType<IrUnknownType>(Field(Procedure("SELECT t.id AS a FROM dbo.t AS t INTERSECT SELECT HASHBYTES('SHA2_256', t.label) FROM dbo.t AS t;"), "a").Type);
    }

    [Fact]
    public void A_column_whose_queries_are_all_of_its_type_keeps_the_facets_they_share()
    {
        // Two queries of varchar(10) (the CHECK set is each one's): the column is varchar(10) with the union of their sets.
        var doc = Procedure("SELECT t.status FROM dbo.t AS t UNION SELECT t.status FROM dbo.t AS t WHERE t.id = 1;");
        Assert.Equal("varchar(10) nullable-column", Typed(doc, "status"));
        Assert.Equal("collation:open|shut", Values(doc, "status"));
    }

    [Fact]
    public void Literals_of_every_query_make_an_exact_set_for_union()
    {
        var doc = Procedure("SELECT 'open' AS s, 1 AS n UNION ALL SELECT 'closed', 2 UNION SELECT 'open', 3;");
        Assert.Equal("varchar(6)", Typed(doc, "s"));
        Assert.Equal("exact:closed|open", Values(doc, "s"));
        Assert.Equal("exact:1|2|3", Values(doc, "n"));
    }

    [Fact]
    public void A_set_is_left_out_when_a_query_has_none_or_a_fixed_length_type_would_pad_it()
    {
        Assert.Null(Values(Procedure("SELECT 'open' AS s UNION ALL SELECT t.short FROM dbo.t AS t;"), "s"));

        // char(4) with a varchar literal is varchar(4), each value its own; char(4) with char(2) pads the shorter.
        Assert.Null(Values(Procedure("SELECT t.code AS s FROM dbo.t AS t UNION ALL SELECT CAST('x' AS char(2));"), "s"));

        // Matches of different kinds (a CHECK's collation set and a literal) are not merged.
        Assert.Null(Values(Procedure("SELECT t.status AS s FROM dbo.t AS t UNION ALL SELECT 'other';"), "s"));
    }

    [Fact]
    public void Intersect_and_except_keep_the_left_sides_set()
    {
        // Intersect_and_except_return_the_left_sides_values_and_compare_under_the_collation.
        Assert.Equal("exact:a|b", Values(Procedure("SELECT 'a' AS s UNION SELECT 'b' INTERSECT SELECT t.short FROM dbo.t AS t;"), "s"));
        Assert.Equal("exact:a", Values(Procedure("SELECT 'a' AS s EXCEPT SELECT 'b';"), "s"));
        Assert.Equal("collation:open|shut", Values(Procedure("SELECT t.status FROM dbo.t AS t INTERSECT SELECT t.short FROM dbo.t AS t;"), "status"));
    }

    [Fact]
    public void Union_is_null_when_any_query_is()
    {
        // Union_all_keeps_every_row_union_removes_duplicates_and_both_keep_a_null.
        var doc = Procedure("SELECT t.id, t.label FROM dbo.t AS t UNION SELECT u.id, u.note FROM dbo.u AS u;");
        Assert.Equal("int", Typed(doc, "id"));
        Assert.Equal("varchar(20) nullable-column", Typed(doc, "label"));
    }

    [Fact]
    public void Intersect_is_never_null_when_either_side_is_never_null_and_except_is_the_left_sides()
    {
        // Intersect_and_except_treat_nulls_as_equal_so_intersect_with_a_non_null_side_has_no_null.
        Assert.Equal("int", Typed(Procedure("SELECT t.x FROM dbo.t AS t INTERSECT SELECT u.qty FROM dbo.u AS u;"), "x"));
        Assert.Equal("int", Typed(Procedure("SELECT u.qty FROM dbo.u AS u INTERSECT SELECT t.x FROM dbo.t AS t;"), "qty"));
        Assert.Equal("int nullable-column", Typed(Procedure("SELECT t.x FROM dbo.t AS t INTERSECT SELECT t.x FROM dbo.t AS t;"), "x"));
        Assert.Equal("int nullable-column", Typed(Procedure("SELECT t.x FROM dbo.t AS t EXCEPT SELECT u.qty FROM dbo.u AS u;"), "x"));
        Assert.Equal("int", Typed(Procedure("SELECT u.qty FROM dbo.u AS u EXCEPT SELECT t.x FROM dbo.t AS t;"), "qty"));
    }

    [Fact]
    public void Intersect_is_applied_before_union_and_except()
    {
        // Intersect_binds_tighter_than_union_and_except_whatever_the_parser_shows: x UNION (qty INTERSECT qty) keeps x's null.
        // Read left to right, as the parser gives it, (x UNION qty) INTERSECT qty would be never null.
        Assert.Equal("int nullable-column", Typed(Procedure("SELECT t.x FROM dbo.t AS t UNION SELECT u.qty FROM dbo.u AS u INTERSECT SELECT u.qty FROM dbo.u AS u;"), "x"));
        Assert.Equal("int nullable-column", Typed(Procedure("SELECT t.x FROM dbo.t AS t EXCEPT SELECT u.qty FROM dbo.u AS u INTERSECT SELECT u.qty FROM dbo.u AS u;"), "x"));

        // Parenthesised, the union is first.
        Assert.Equal("int", Typed(Procedure("(SELECT t.x FROM dbo.t AS t UNION SELECT u.qty FROM dbo.u AS u) INTERSECT SELECT u.qty FROM dbo.u AS u;"), "x"));
    }

    [Fact]
    public void Each_querys_filter_refines_its_own_columns()
    {
        var doc = Procedure("SELECT t.x FROM dbo.t AS t WHERE t.x > 0 UNION ALL SELECT u.qty FROM dbo.u AS u;");
        Assert.Equal("int", Typed(doc, "x"));
        doc = Procedure("SELECT t.x FROM dbo.t AS t WHERE t.x > 0 UNION ALL SELECT t.x FROM dbo.t AS t;");
        Assert.Equal("int nullable-column", Typed(doc, "x"));
    }

    [Theory]
    [InlineData("SELECT t.id FROM dbo.t AS t WHERE t.id = 1 UNION SELECT t.id FROM dbo.t AS t WHERE t.id = 2;", IrCardinality.Many)]
    [InlineData("SELECT COUNT(*) AS n FROM dbo.t UNION ALL SELECT COUNT(*) FROM dbo.u;", IrCardinality.Many)]
    [InlineData("SELECT t.id FROM dbo.t AS t WHERE t.id = 1 INTERSECT SELECT u.id FROM dbo.u AS u;", IrCardinality.AtMostOne)]
    [InlineData("SELECT u.id FROM dbo.u AS u INTERSECT SELECT t.id FROM dbo.t AS t WHERE t.id = 1;", IrCardinality.AtMostOne)]
    [InlineData("SELECT COUNT(*) AS n FROM dbo.t INTERSECT SELECT COUNT(*) FROM dbo.u;", IrCardinality.AtMostOne)]
    [InlineData("SELECT t.id FROM dbo.t AS t WHERE t.id = 1 EXCEPT SELECT u.id FROM dbo.u AS u;", IrCardinality.AtMostOne)]
    [InlineData("SELECT u.id FROM dbo.u AS u EXCEPT SELECT t.id FROM dbo.t AS t WHERE t.id = 1;", IrCardinality.Many)]
    [InlineData("SELECT t.id FROM dbo.t AS t UNION ALL SELECT u.id FROM dbo.u AS u ORDER BY id OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY;", IrCardinality.AtMostOne)]
    [InlineData("SELECT TOP (1) t.id FROM dbo.t AS t UNION ALL SELECT TOP (1) u.id FROM dbo.u AS u;", IrCardinality.Many)]
    public void A_set_operation_is_at_most_one_row_only_where_proved(string query, IrCardinality expected) =>
        // Order_by_and_offset_apply_to_the_whole_set_operation_and_top_to_its_first_query.
        Assert.Equal(expected, Rows(Procedure(query)));

    [Fact]
    public void A_key_does_not_pass_through_a_union_in_a_view()
    {
        var doc = Procedure(
            "SELECT v.id FROM dbo.v AS v WHERE v.id = 1;",
            "CREATE VIEW dbo.v AS SELECT t.id FROM dbo.t AS t UNION ALL SELECT t.id FROM dbo.t AS t;");
        AllTyped(doc);
        Assert.Equal(IrCardinality.Many, Rows(doc));
    }

    [Theory]
    [InlineData("SELECT d.a FROM (SELECT t.id AS a FROM dbo.t AS t UNION SELECT u.id FROM dbo.u AS u) AS d;")]
    [InlineData("WITH c AS (SELECT t.id AS a FROM dbo.t AS t UNION SELECT u.id FROM dbo.u AS u) SELECT c.a FROM c;")]
    [InlineData("WITH c (a) AS (SELECT t.id FROM dbo.t AS t EXCEPT SELECT u.id FROM dbo.u AS u) SELECT c.a FROM c;")]
    [InlineData("SELECT (SELECT TOP (1) t.id AS a FROM dbo.t AS t UNION ALL SELECT u.id FROM dbo.u AS u) AS a;")]
    [InlineData("SELECT v.a FROM dbo.v AS v;")]
    [InlineData("SELECT f.a FROM dbo.f() AS f;")]
    [InlineData("SELECT t.id AS a INTO #c FROM dbo.t AS t UNION ALL SELECT u.id FROM dbo.u AS u; SELECT c.a FROM #c AS c;")]
    [InlineData("CREATE TABLE #c (a int NOT NULL); INSERT INTO #c (a) SELECT t.id FROM dbo.t AS t UNION SELECT u.id FROM dbo.u AS u; SELECT c.a FROM #c AS c;")]
    public void A_set_operation_is_read_wherever_a_query_stands(string body)
    {
        var doc = Procedure(body, """
            CREATE VIEW dbo.v AS SELECT t.id AS a FROM dbo.t AS t UNION SELECT u.id FROM dbo.u AS u;
            GO
            CREATE FUNCTION dbo.f() RETURNS TABLE AS RETURN SELECT t.id AS a FROM dbo.t AS t UNION SELECT u.id FROM dbo.u AS u;
            GO
            """);
        AllTyped(doc);
        Assert.Equal("int", Sql(doc, "a"));
    }

    [Fact]
    public void A_set_operation_feeds_for_json_as_a_result_and_as_a_subquery()
    {
        var doc = Procedure("SELECT t.id, t.short FROM dbo.t AS t UNION ALL SELECT u.id, u.note FROM dbo.u AS u FOR JSON PATH;");
        AllTyped(doc);
        Assert.Equal(IrEncoding.Json, Results(doc)[0].Encoding);
        Assert.Equal("varchar(20) nullable-column nullable-column", Typed(doc, "short"));
        Assert.Equal(IrPresence.OmittedWhenNull, Field(doc, "short").Presence);

        // In a subquery SQL Server refuses FOR JSON on the set operation itself, so it goes over a derived table
        // (A_for_json_subquery_over_a_set_operation_needs_a_derived_table).
        doc = Procedure("SELECT t.id, (SELECT d.id FROM (SELECT u.id FROM dbo.u AS u WHERE u.t_id = t.id UNION SELECT 0) AS d FOR JSON PATH) AS ids FROM dbo.t AS t;");
        AllTyped(doc);
        Assert.IsType<IrJsonText>(Field(doc, "ids").Type);
    }

    [Fact]
    public void A_recursive_cte_still_reads()
    {
        var doc = Procedure("""
            WITH r AS (SELECT t.id, 0 AS depth FROM dbo.t AS t UNION ALL SELECT r.id, r.depth + 1 FROM r WHERE r.depth < 3)
            SELECT r.id FROM r;
            """);
        AllTyped(doc);
        Assert.Equal("int", Typed(doc, "id"));
    }

    [Fact]
    public void Json_through_a_set_operation_is_not_modelled()
    {
        // Json_through_a_set_operation_is_text_except_json_query.
        var doc = Procedure("SELECT (SELECT u.id FROM dbo.u AS u FOR JSON PATH) AS j UNION ALL SELECT (SELECT u.qty FROM dbo.u AS u FOR JSON PATH);");
        Assert.IsType<IrUnknownType>(Field(doc, "j").Type);

        // Text holding JSON stays text whose content is the same.
        doc = Procedure("SELECT (SELECT u.id FROM dbo.u AS u FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) AS j UNION ALL SELECT (SELECT u.id FROM dbo.u AS u FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);");
        Assert.IsType<IrJsonText>(Field(doc, "j").Type);
    }
}
