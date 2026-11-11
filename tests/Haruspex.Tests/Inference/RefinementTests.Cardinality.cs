using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

public partial class RefinementTests
{
    [Theory]
    [InlineData("SELECT COUNT(*) AS n FROM dbo.t AS t;", IrCardinality.One)]
    [InlineData("SELECT COUNT(*) AS n FROM dbo.t AS t WHERE t.n > 1;", IrCardinality.One)]
    [InlineData("SELECT MAX(t.id) AS n, COUNT(*) AS c FROM dbo.t AS t;", IrCardinality.One)]
    [InlineData("SELECT COUNT(*) AS n FROM dbo.t AS t HAVING COUNT(*) > 1;", IrCardinality.AtMostOne)]
    [InlineData("SELECT COUNT(*) AS n FROM dbo.t AS t GROUP BY t.n;", IrCardinality.Many)]
    [InlineData("SELECT COUNT(*) AS n FROM dbo.t AS t GROUP BY ();", IrCardinality.Many)]
    [InlineData("SELECT 1 AS n;", IrCardinality.One)]
    [InlineData("SELECT 1 AS n WHERE @x = 1;", IrCardinality.AtMostOne)]
    [InlineData("SELECT t.id FROM dbo.t AS t;", IrCardinality.Many)]
    [InlineData("SELECT TOP (1) t.id FROM dbo.t AS t ORDER BY t.id;", IrCardinality.AtMostOne)]
    [InlineData("SELECT TOP 1 t.id FROM dbo.t AS t;", IrCardinality.AtMostOne)]
    [InlineData("SELECT TOP (1) WITH TIES t.id FROM dbo.t AS t ORDER BY t.n;", IrCardinality.Many)]
    [InlineData("SELECT TOP (1) PERCENT t.id FROM dbo.t AS t;", IrCardinality.Many)]
    [InlineData("SELECT TOP (@x) t.id FROM dbo.t AS t;", IrCardinality.Many)]
    [InlineData("SELECT TOP (2) t.id FROM dbo.t AS t;", IrCardinality.Many)]
    [InlineData("SELECT TOP (1) COUNT(*) AS n FROM dbo.t AS t;", IrCardinality.One)]
    [InlineData("SELECT TOP (@x) COUNT(*) AS n FROM dbo.t AS t;", IrCardinality.AtMostOne)]
    [InlineData("SELECT t.id FROM dbo.t AS t ORDER BY t.id OFFSET 5 ROWS FETCH NEXT 1 ROWS ONLY;", IrCardinality.AtMostOne)]
    [InlineData("SELECT t.id FROM dbo.t AS t ORDER BY t.id OFFSET 0 ROWS FETCH NEXT 2 ROWS ONLY;", IrCardinality.Many)]
    [InlineData("SELECT COUNT(*) AS n FROM dbo.t AS t ORDER BY 1 OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY;", IrCardinality.One)]
    [InlineData("SELECT COUNT(*) AS n FROM dbo.t AS t ORDER BY 1 OFFSET 1 ROWS;", IrCardinality.AtMostOne)]
    public void How_many_rows_a_query_returns(string query, IrCardinality expected) =>
        // An_aggregate_without_group_by_returns_exactly_one_row_even_over_no_rows, An_aggregate_with_group_by_returns_no_row_over_no_rows,
        // Having_over_no_groups_returns_no_row_and_without_group_by_at_most_one, Top_1_and_fetch_1_give_at_most_one_row_but_with_ties_gives_more.
        Assert.Equal(expected, Rows(query, "@x int"));

    [Theory]
    [InlineData("SELECT t.name FROM dbo.t AS t WHERE t.id = @x;", IrCardinality.AtMostOne)]
    [InlineData("SELECT t.name FROM dbo.t AS t WHERE @x = t.id AND t.n > 0;", IrCardinality.AtMostOne)]
    [InlineData("SELECT t.name FROM dbo.t AS t WHERE t.id = 5;", IrCardinality.AtMostOne)]
    [InlineData("SELECT t.name FROM dbo.t AS t WHERE t.id = @big;", IrCardinality.AtMostOne)]
    [InlineData("SELECT t.name FROM dbo.t AS t WHERE t.code = @code;", IrCardinality.AtMostOne)]
    [InlineData("SELECT t.name FROM dbo.t AS t WHERE t.code = 'ab';", IrCardinality.AtMostOne)]
    [InlineData("SELECT t.name FROM dbo.t AS t WHERE t.code IS NULL;", IrCardinality.AtMostOne)]
    [InlineData("SELECT p.v FROM dbo.pair AS p WHERE p.a = 1 AND p.b = @x;", IrCardinality.AtMostOne)]
    [InlineData("SELECT p.a FROM dbo.pair AS p WHERE p.v IS NULL AND p.w = 'x';", IrCardinality.AtMostOne)]
    [InlineData("SELECT u.note FROM dbo.u AS u JOIN dbo.t AS t ON t.id = u.t_id WHERE u.id = @x;", IrCardinality.AtMostOne)]
    [InlineData("SELECT u.note FROM dbo.u AS u LEFT JOIN dbo.t AS t ON t.id = u.t_id WHERE u.id = @x;", IrCardinality.AtMostOne)]
    [InlineData("SELECT u.note FROM dbo.u AS u, dbo.t AS t WHERE t.id = u.t_id AND u.id = @x;", IrCardinality.AtMostOne)]
    [InlineData("SELECT t.n, COUNT(*) AS c FROM dbo.t AS t WHERE t.id = @x GROUP BY t.n;", IrCardinality.AtMostOne)]
    public void A_filter_fixing_a_whole_key_returns_at_most_one_row(string query, IrCardinality expected) =>
        // A_unique_constraint_allows_one_null_only: so `code IS NULL` fixes at most one row too.
        Assert.Equal(expected, Rows(query, "@x int, @big bigint, @code varchar(10)"));

    [Theory]
    [InlineData("SELECT p.v FROM dbo.pair AS p WHERE p.a = 1;")]
    [InlineData("SELECT t.name FROM dbo.t AS t WHERE t.id = @x OR t.id = 2;")]
    [InlineData("SELECT t.name FROM dbo.t AS t WHERE t.id > @x;")]
    [InlineData("SELECT t.name FROM dbo.t AS t WHERE t.id IN (1, 2);")]
    [InlineData("SELECT t.name FROM dbo.t AS t WHERE t.id = t.n;")]
    [InlineData("SELECT t.name FROM dbo.t AS t WHERE t.id + 0 = @x;")]
    [InlineData("SELECT t.name FROM dbo.t AS t WHERE t.code = 5;")]
    [InlineData("SELECT t.name FROM dbo.t AS t WHERE t.code = N'ab';")]
    [InlineData("SELECT t.name FROM dbo.t AS t WHERE t.code = @ncode;")]
    [InlineData("SELECT t.name FROM dbo.t AS t WHERE t.code = 'ab' COLLATE Latin1_General_BIN;")]
    [InlineData("SELECT t.name FROM dbo.t AS t WHERE t.id IS NULL OR t.id = @x;")]
    [InlineData("SELECT u.note FROM dbo.u AS u JOIN dbo.t AS t ON t.n = u.t_id WHERE u.id = @x;")]
    [InlineData("SELECT u.note FROM dbo.u AS u JOIN dbo.t AS t ON t.id = u.t_id;")]
    [InlineData("SELECT t.name FROM dbo.t AS t JOIN dbo.u AS u ON u.id = @x WHERE t.id = u.t_id OR 1 = 1;")]
    [InlineData("SELECT t.name FROM dbo.t AS t LEFT JOIN dbo.u AS u ON t.id = @x;")]
    public void A_filter_that_does_not_fix_a_whole_key_in_the_keys_own_type_returns_many(string query) =>
        // A_comparison_that_converts_the_key_can_match_several_rows, A_varchar_key_under_a_sql_collation_compared_with_nvarchar_can_match_several_rows.
        Assert.Equal(IrCardinality.Many, Rows(query, "@x int, @ncode nvarchar(10)"));

    [Theory]
    [InlineData("CREATE TABLE dbo.k (id int NOT NULL, code int NOT NULL, INDEX ix_k_code UNIQUE (code));", IrCardinality.AtMostOne)]
    [InlineData("CREATE TABLE dbo.k (id int NOT NULL, code int NOT NULL, INDEX ix_k_code UNIQUE (code) WHERE code > 0);", IrCardinality.Many)]
    [InlineData("CREATE TABLE dbo.k (id int NOT NULL, code int NOT NULL, INDEX ix_k_code (code));", IrCardinality.Many)]
    [InlineData("CREATE TABLE dbo.k (id int NOT NULL, code int NOT NULL CONSTRAINT uq_k_code UNIQUE);", IrCardinality.AtMostOne)]
    [InlineData("CREATE TABLE dbo.k (id int NOT NULL, code int NOT NULL);", IrCardinality.Many)]
    public void Which_declarations_make_a_key(string table, IrCardinality expected)
    {
        // A filtered unique index is unique only among the rows its filter keeps.
        var doc = Infer(("k.sql", table), ("p.sql", "CREATE PROCEDURE dbo.p @x int AS SELECT k.id FROM dbo.k AS k WHERE k.code = @x;"));

        Assert.Equal(expected, Assert.Single(Results(doc)).Cardinality);
    }

    [Theory]
    [InlineData("ALTER INDEX uq_t_code ON dbo.t DISABLE;", IrCardinality.Many, IrCardinality.AtMostOne)]
    [InlineData("ALTER INDEX ALL ON dbo.t DISABLE;", IrCardinality.Many, IrCardinality.Many)]
    [InlineData("ALTER INDEX ix_other ON dbo.u DISABLE;", IrCardinality.AtMostOne, IrCardinality.AtMostOne)]
    [InlineData("CREATE PROCEDURE dbo.load AS ALTER INDEX uq_t_code ON t DISABLE;", IrCardinality.Many, IrCardinality.AtMostOne)]
    public void A_disabled_index_is_no_key(string disable, IrCardinality byCode, IrCardinality byId)
    {
        // A disabled unique index enforces nothing, so rows written meanwhile may share a key; wherever the statement is.
        var doc = Infer(("tables.sql", Tables), ("disable.sql", disable),
            ("p.sql", "CREATE PROCEDURE dbo.p @code varchar(10), @x int AS SELECT t.name FROM dbo.t AS t WHERE t.code = @code; SELECT t.name FROM dbo.t AS t WHERE t.id = @x;"));

        var results = doc.Objects.Single(o => o.Id == "dbo.p").Results!;
        Assert.Equal(byCode, results[0].Cardinality);
        Assert.Equal(byId, results[1].Cardinality);
    }

    [Fact]
    public void For_json_over_one_row_is_never_absent()
    {
        // For_json_over_an_aggregate_without_group_by_always_returns_its_one_object.
        var result = Assert.Single(Results(Procedure("SELECT COUNT_BIG(*) AS n FROM dbo.t AS t FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;")));

        Assert.Equal(IrCardinality.One, result.Cardinality);
        Assert.False(result.MayBeAbsent);
    }

    [Fact]
    public void For_json_by_key_may_be_absent_and_returns_at_most_one_object()
    {
        var result = Assert.Single(Results(Procedure("SELECT t.name FROM dbo.t AS t WHERE t.id = @x FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;", "@x int")));

        Assert.Equal(IrCardinality.AtMostOne, result.Cardinality);
        Assert.True(result.MayBeAbsent);
    }

    [Fact]
    public void Results_at_one_position_join_their_cardinalities()
    {
        var doc = Procedure("""
            IF @x = 1 SELECT COUNT(*) AS n FROM dbo.t AS t;
            ELSE SELECT t.id AS n FROM dbo.t AS t WHERE t.id = @x;
            IF @x = 2 SELECT COUNT(*) AS n FROM dbo.t AS t; ELSE SELECT COUNT_BIG(*) AS n FROM dbo.t AS t;
            """, "@x int");

        Assert.Equal([IrCardinality.AtMostOne, IrCardinality.One], Results(doc).Select(r => r.Cardinality));
    }

    [Fact]
    public void A_callees_result_keeps_its_cardinality()
    {
        var doc = Infer(("tables.sql", Tables),
            ("q.sql", "CREATE PROCEDURE dbo.q @x int AS SELECT t.name FROM dbo.t AS t WHERE t.id = @x;"),
            ("p.sql", "CREATE PROCEDURE dbo.p AS EXEC dbo.q 1;"));

        Assert.All(doc.Objects, o => Assert.Equal(IrCardinality.AtMostOne, Assert.Single(o.Results!).Cardinality));
    }
}
