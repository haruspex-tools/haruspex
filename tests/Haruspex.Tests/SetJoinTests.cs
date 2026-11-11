using Haruspex.Cli;
using Haruspex.Ir;

namespace Haruspex.Tests;

/// <summary>
/// Set operations (<c>UNION</c>, <c>UNION ALL</c>, <c>INTERSECT</c>, <c>EXCEPT</c>), <c>CROSS JOIN</c>, <c>FULL OUTER JOIN</c> and
/// <c>OPENJSON</c> (slice 6h). Each rule rests on an observation in the differential tests' <c>SetJoinObservationTests</c>, named
/// where it matters.
/// </summary>
public class SetJoinTests
{
    private const string Tables = """
        CREATE TABLE dbo.t (
            id int NOT NULL PRIMARY KEY, x int NULL, tiny tinyint NULL, amount decimal(9,2) NULL, code char(4) NULL,
            short varchar(10) NULL, label varchar(8) NOT NULL, big bigint NOT NULL, doc nvarchar(max) NULL,
            status varchar(10) NULL CONSTRAINT ck_status CHECK (status IN ('open', 'shut')));
        GO
        CREATE TABLE dbo.u (id int NOT NULL PRIMARY KEY, t_id int NOT NULL, qty int NOT NULL, note varchar(20) NULL);
        GO
        """;

    private static IrDocument Infer(params (string File, string Sql)[] files)
    {
        using var dir = new TempDirectory();
        foreach (var (file, sql) in files)
            dir.File(file, sql);
        var project = SqlProject.Load(dir.Path);
        Assert.Empty(project.Diagnostics);
        return project.Infer(project.Select([ObjectPattern.Parse("dbo.p")]).Procedures);
    }

    private static IrDocument Procedure(string body, string others = "") =>
        others.Length == 0
            ? Infer(("tables.sql", Tables), ("p.sql", $"CREATE PROCEDURE dbo.p AS\n{body}"))
            : Infer(("tables.sql", Tables), ("others.sql", others), ("p.sql", $"CREATE PROCEDURE dbo.p AS\n{body}"));

    private static IReadOnlyList<IrResult> Results(IrDocument doc) =>
        doc.Objects[0].Results ?? throw new InvalidOperationException("The results are unknown: " + string.Join("; ", doc.Unknowns.Select(u => u.Detail)));

    private static IReadOnlyList<IrField> Fields(IrDocument doc, IrType type) => type switch
    {
        IrArray { Items: var items } => Fields(doc, items),
        IrRef r => doc.Types[r.Name].Fields,
        var other => throw new InvalidOperationException($"Unexpected type {other}."),
    };

    private static IrField Field(IrDocument doc, string name) => Assert.Single(Fields(doc, Results(doc)[0].Type), f => f.Name == name);

    private static string[] Rules(IrDocument doc, string name) => [.. Field(doc, name).NullableBecause.Select(r => r.Rule)];

    private static string Sql(IrDocument doc, string name) => Field(doc, name).Type switch
    {
        IrScalar scalar => scalar.Sql ?? "(none)",
        var other => other.GetType().Name,
    };

    /// <summary>The type and the reasons to be null, as one string: <c>varchar(6) nullable-column</c>.</summary>
    private static string Typed(IrDocument doc, string name) => string.Join(" ", [Sql(doc, name), .. Rules(doc, name)]);

    private static string? Values(IrDocument doc, string name) =>
        Field(doc, name).Type is IrScalar { Facets.Values: { } set }
            ? $"{set.Match.ToString().ToLowerInvariant()}:{string.Join("|", set.Items.Select(i => i.Text))}"
            : null;

    private static void AllTyped(IrDocument doc) =>
        Assert.True(doc.Unknowns.Count == 0, "Unknowns: " + string.Join("; ", doc.Unknowns.Select(u => $"{u.Path}: {u.Detail}")));

    private static IrCardinality Rows(IrDocument doc) => Results(doc)[0].Cardinality;

    // ---- set operations: names and types ---------------------------------------------------------------------------------

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
        Assert.IsType<IrUnknownType>(Field(Procedure("SELECT t.id AS a FROM dbo.t AS t INTERSECT SELECT SOUNDEX(t.label) FROM dbo.t AS t;"), "a").Type);
    }

    [Fact]
    public void A_column_whose_queries_are_all_of_its_type_keeps_the_facets_they_share()
    {
        // Two queries of varchar(10) (the CHECK set is each one's): the column is varchar(10) with the union of their sets.
        var doc = Procedure("SELECT t.status FROM dbo.t AS t UNION SELECT t.status FROM dbo.t AS t WHERE t.id = 1;");
        Assert.Equal("varchar(10) nullable-column", Typed(doc, "status"));
        Assert.Equal("collation:open|shut", Values(doc, "status"));
    }

    // ---- set operations: closed value sets -------------------------------------------------------------------------------

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

    // ---- set operations: nulls -------------------------------------------------------------------------------------------

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

    // ---- set operations: rows --------------------------------------------------------------------------------------------

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

    // ---- set operations: wherever a query stands ---------------------------------------------------------------------------

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

    // ---- CROSS JOIN ------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_cross_join_reads_both_sides_as_they_are()
    {
        var doc = Procedure("SELECT t.id, t.x, u.note FROM dbo.t AS t CROSS JOIN dbo.u AS u;");
        AllTyped(doc);
        Assert.Equal("int", Typed(doc, "id"));
        Assert.Equal("int nullable-column", Typed(doc, "x"));
        Assert.Equal("varchar(20) nullable-column", Typed(doc, "note"));
        Assert.Equal(IrCardinality.Many, Rows(doc));
    }

    [Theory]
    [InlineData("SELECT a.n, b.m FROM (SELECT COUNT(*) AS n FROM dbo.t) AS a CROSS JOIN (SELECT COUNT(*) AS m FROM dbo.u) AS b;", IrCardinality.One)]
    [InlineData("SELECT a.n, b.m FROM (SELECT COUNT(*) AS n FROM dbo.t) AS a CROSS JOIN (SELECT COUNT(*) AS m FROM dbo.u) AS b WHERE a.n > 0;", IrCardinality.AtMostOne)]
    [InlineData("SELECT a.n, b.id FROM (SELECT COUNT(*) AS n FROM dbo.t) AS a CROSS JOIN dbo.u AS b WHERE b.id = 1;", IrCardinality.AtMostOne)]
    [InlineData("SELECT a.n, b.id FROM (SELECT COUNT(*) AS n FROM dbo.t) AS a CROSS JOIN dbo.u AS b;", IrCardinality.Many)]
    [InlineData("SELECT a.n, b.m FROM (SELECT COUNT(*) AS n FROM dbo.t) AS a CROSS JOIN (SELECT TOP (1) u.id AS m FROM dbo.u AS u) AS b;", IrCardinality.AtMostOne)]
    [InlineData("SELECT a.n, b.m FROM (SELECT COUNT(*) AS n FROM dbo.t) AS a CROSS JOIN (SELECT COUNT(*) AS m FROM dbo.u) AS b, (SELECT 1 AS k) AS c;", IrCardinality.One)]
    public void A_cross_join_is_one_row_only_when_both_sides_are(string query, IrCardinality expected) =>
        // A_cross_join_of_two_one_row_queries_is_one_row_and_with_an_empty_side_none.
        Assert.Equal(expected, Rows(Procedure(query)));

    // ---- FULL OUTER JOIN -------------------------------------------------------------------------------------------------

    [Fact]
    public void A_full_join_makes_both_sides_nullable()
    {
        // A_full_outer_join_null_extends_both_sides_where_nothing_matches.
        var doc = Procedure("SELECT t.id AS t_id, t.x, u.id AS u_id FROM dbo.t AS t FULL OUTER JOIN dbo.u AS u ON u.t_id = t.id;");
        AllTyped(doc);
        Assert.Equal("int outer-join", Typed(doc, "t_id"));
        Assert.Equal("int nullable-column outer-join", Typed(doc, "x"));
        Assert.Equal("int outer-join", Typed(doc, "u_id"));
        Assert.Equal(IrCardinality.Many, Rows(doc));
    }

    [Fact]
    public void A_full_joins_on_proves_nothing_of_either_side()
    {
        // A_full_outer_joins_on_proves_nothing_of_either_side.
        var doc = Procedure("SELECT t.x, u.note FROM dbo.t AS t FULL JOIN dbo.u AS u ON u.t_id = t.id AND t.x IS NOT NULL AND u.note IS NOT NULL;");
        Assert.Equal("int nullable-column outer-join", Typed(doc, "x"));
        Assert.Equal("varchar(20) nullable-column outer-join", Typed(doc, "note"));
    }

    [Fact]
    public void A_where_on_one_side_of_a_full_join_proves_that_sides_row()
    {
        // A_where_on_one_side_of_a_full_join_drops_that_sides_unmatched_rows_only.
        var doc = Procedure("SELECT t.id AS t_id, t.x, u.id AS u_id FROM dbo.t AS t FULL JOIN dbo.u AS u ON u.t_id = t.id WHERE t.id > 0;");
        Assert.Equal("int", Typed(doc, "t_id"));
        Assert.Equal("int nullable-column", Typed(doc, "x"));
        Assert.Equal("int outer-join", Typed(doc, "u_id"));

        // An inner join after it on the other side proves that one.
        doc = Procedure("SELECT t.id AS t_id, u.id AS u_id FROM dbo.t AS t FULL JOIN dbo.u AS u ON u.t_id = t.id JOIN dbo.u AS w ON w.id = u.id;");
        Assert.Equal("int outer-join", Typed(doc, "t_id"));
        Assert.Equal("int", Typed(doc, "u_id"));
    }

    [Theory]
    [InlineData("SELECT a.id, b.id AS b_id FROM (SELECT t.id FROM dbo.t AS t WHERE t.id = 1) AS a FULL JOIN (SELECT u.id FROM dbo.u AS u WHERE u.id = 4) AS b ON b.id = a.id;")]
    [InlineData("SELECT t.id, u.id AS b_id FROM dbo.t AS t FULL JOIN dbo.u AS u ON u.id = t.id WHERE u.id IS NULL AND t.id = 1;")]
    [InlineData("SELECT t.id, u.id AS b_id FROM dbo.t AS t FULL JOIN dbo.u AS u ON u.id = t.id WHERE t.id = 1;")]
    public void A_full_join_is_never_bounded_by_its_sides(string query) =>
        // A_full_join_of_two_queries_of_at_most_one_row_can_return_two.
        Assert.Equal(IrCardinality.Many, Rows(Procedure(query)));

    [Fact]
    public void Right_join_stays_unmodelled()
    {
        var doc = Procedure("SELECT t.id FROM dbo.t AS t RIGHT JOIN dbo.u AS u ON u.t_id = t.id;");
        Assert.IsType<IrUnknownType>(Field(doc, "id").Type);
    }

    // ---- OPENJSON --------------------------------------------------------------------------------------------------------

    [Fact]
    public void Openjson_without_with_is_key_value_and_type()
    {
        // Openjson_without_with_gives_key_value_and_type.
        var doc = Procedure("SELECT j.[key], j.value, j.type FROM dbo.t AS t CROSS APPLY OPENJSON(t.doc) AS j;");
        AllTyped(doc);
        Assert.Equal("nvarchar(4000)", Typed(doc, "key"));
        Assert.Equal("nvarchar(max) json-value", Typed(doc, "value"));
        Assert.Equal("tinyint", Typed(doc, "type"));
        Assert.Equal("exact:0|1|2|3|4|5", Values(doc, "type"));
    }

    [Fact]
    public void Openjson_with_columns_are_of_their_declared_types_and_may_be_null_even_under_strict()
    {
        // Openjson_with_columns_are_of_their_declared_types_and_null_for_a_missing_path_or_a_json_null.
        var doc = Procedure("""
            DECLARE @doc nvarchar(max) = N'[]';
            SELECT j.a, j.s, j.d FROM OPENJSON(@doc) WITH (a int '$.a', s varchar(3) 'strict $.s', d date) AS j;
            """);
        AllTyped(doc);
        Assert.Equal("int json-value", Typed(doc, "a"));
        Assert.Equal("varchar(3) json-value", Typed(doc, "s"));
        Assert.Equal("date json-value", Typed(doc, "d"));
    }

    [Fact]
    public void Openjson_as_json_nests_as_json_of_unknown_shape()
    {
        // Openjson_as_json_is_nvarchar_max_holding_an_object_or_array_that_nests_as_json.
        var doc = Procedure("SELECT j.items FROM dbo.t AS t CROSS APPLY OPENJSON(t.doc) WITH (items nvarchar(max) '$.items' AS JSON) AS j;");
        Assert.IsType<IrJsonText>(Field(doc, "items").Type);
        Assert.Equal(["json-value"], Rules(doc, "items"));

        doc = Procedure("SELECT j.items FROM dbo.t AS t CROSS APPLY OPENJSON(t.doc) WITH (items nvarchar(max) '$.items' AS JSON) AS j FOR JSON PATH;");
        Assert.IsType<IrUnknownType>(Field(doc, "items").Type);
        Assert.Equal(IrUnknownReasons.JsonShapeUnknown, Assert.Single(doc.Unknowns).Reason);
    }

    [Fact]
    public void Openjson_under_outer_apply_is_null_extended()
    {
        var doc = Procedure("SELECT t.id, j.a FROM dbo.t AS t OUTER APPLY OPENJSON(t.doc) WITH (a int) AS j;");
        Assert.Equal("int json-value outer-apply", Typed(doc, "a"));
        Assert.Equal(IrCardinality.Many, Rows(doc));
    }
}
