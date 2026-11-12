using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

public partial class StatementFlowTests
{
    [Fact]
    public void A_cte_is_read_like_the_query_it_names()
    {
        var doc = Procedure("WITH x AS (SELECT t.id, t.name, t.status FROM dbo.t AS t) SELECT x.id, x.name, x.status FROM x;");

        Assert.Empty(doc.Unknowns);
        Assert.False(Field(doc, "id").Nullable);
        Assert.Equal("dbo.t.name", Assert.Single(Field(doc, "name").NullableBecause).At);
        Assert.NotNull(Scalar(Field(doc, "status")).Facets!.Values);
    }

    [Fact]
    public void A_ctes_column_list_renames_its_columns()
    {
        var doc = Procedure("WITH x (k, v) AS (SELECT t.id, t.name FROM dbo.t AS t) SELECT x.k, v FROM x;");

        Assert.False(Field(doc, "k").Nullable);
        Assert.True(Field(doc, "v").Nullable);
    }

    [Fact]
    public void A_cte_may_read_an_earlier_one_in_the_same_with()
    {
        var doc = Procedure("""
            WITH a AS (SELECT t.id, t.name FROM dbo.t AS t),
                 b AS (SELECT a.id, u.name FROM a JOIN dbo.u AS u ON u.t_id = a.id)
            SELECT b.id, b.name FROM b;
            """);

        Assert.Empty(doc.Unknowns);
        Assert.False(Field(doc, "name").Nullable);
    }

    [Fact]
    public void A_cte_hides_a_table_of_the_same_name_unless_the_schema_is_written()
    {
        var doc = Procedure("WITH u AS (SELECT t.name FROM dbo.t AS t) SELECT u.name FROM u;\nWITH u AS (SELECT t.name FROM dbo.t AS t) SELECT x.name FROM dbo.u AS x;");

        Assert.True(Field(doc, "name", 0).Nullable);
        Assert.False(Field(doc, "name", 1).Nullable);
    }

    [Fact]
    public void A_cte_keeps_json_live()
    {
        // Observed: Json_stored_in_a_temporary_table_is_text_but_json_through_a_cte_stays_json.
        var doc = Procedure("WITH c AS (SELECT t.id, json_object('n': t.name) AS j FROM dbo.t AS t) SELECT c.id, c.j FROM c FOR JSON PATH;");

        Assert.IsType<IrRef>(Field(doc, "j").Type);
    }

    [Fact]
    public void A_cte_can_feed_select_into_and_insert()
    {
        var doc = Procedure("""
            WITH a AS (SELECT t.id FROM dbo.t AS t) SELECT a.id INTO #t FROM a;
            WITH b AS (SELECT u.id FROM dbo.u AS u) INSERT INTO #t (id) SELECT b.id FROM b;
            SELECT x.id FROM #t AS x;
            """);

        Assert.False(Field(doc, "id").Nullable);
    }

    [Fact]
    public void A_cte_whose_query_is_a_union_is_read_as_a_set_operation()
    {
        // SetJoinTests has the rules.
        var doc = Procedure("WITH x AS (SELECT t.id FROM dbo.t AS t UNION SELECT u.id FROM dbo.u AS u) SELECT x.id FROM x;");

        Assert.Empty(doc.Unknowns);
    }

    [Fact]
    public void A_recursive_cte_types_its_columns_from_the_anchor_and_joins_nullability_to_a_fixed_point()
    {
        // b is never null in the anchor, but the recursive member writes note into it, which can be; a is fed b by the
        // recursive member, so it can be null only from the second pass.
        var doc = Procedure("""
            WITH w (id, a, b, depth) AS (
                SELECT n.id, n.label, n.label, 0 FROM dbo.node AS n WHERE n.parent_id IS NULL
                UNION ALL
                SELECT c.id, w.b, c.note, w.depth + 1 FROM dbo.node AS c JOIN w ON c.parent_id = w.id)
            SELECT w.id, w.a, w.b, w.depth FROM w;
            """);

        Assert.False(Field(doc, "id").Nullable);
        Assert.Equal("nvarchar(20)", Scalar(Field(doc, "a")).Sql);
        Assert.True(Field(doc, "a").Nullable);
        Assert.True(Field(doc, "b").Nullable);

        // A recursive column has its anchor's type exactly (observed: A_recursive_member_of_another_type_than_the_anchor_is_an_error).
        // The recursive member's depth + 1 is an int that can overflow, so it is null only for that.
        Assert.Equal("int", Scalar(Field(doc, "depth")).Sql);
        Assert.Equal([IrNullRule.ArithmeticOverflow], Rules(Field(doc, "depth")));
    }

    [Fact]
    public void A_hierarchy_walked_by_a_recursive_cte_types_where_every_member_reads_columns()
    {
        var doc = Procedure("""
            WITH tree AS (
                SELECT n.id, n.parent_id, n.label FROM dbo.node AS n WHERE n.parent_id IS NULL
                UNION ALL
                SELECT c.id, c.parent_id, c.label FROM dbo.node AS c JOIN tree AS p ON c.parent_id = p.id)
            SELECT tree.id, tree.parent_id, tree.label FROM tree FOR JSON PATH;
            """);

        Assert.Empty(doc.Unknowns);
        Assert.False(Field(doc, "label").Nullable);
        Assert.Equal(20, Scalar(Field(doc, "label")).Facets!.Length!.Value);
        Assert.True(Field(doc, "parent_id").Nullable);
    }
}
