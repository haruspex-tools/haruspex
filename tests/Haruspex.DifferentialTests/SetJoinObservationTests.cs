using Haruspex.DifferentialTests.Server;

namespace Haruspex.DifferentialTests;

/// <summary>
/// What SQL Server does with set operations (<c>UNION</c>, <c>UNION ALL</c>, <c>INTERSECT</c>, <c>EXCEPT</c>), <c>CROSS JOIN</c>,
/// <c>FULL OUTER JOIN</c> and <c>OPENJSON</c>, observed and kept as tests: the rules haruspex's reading of them rests on (Haruspex.SqlServer.Inference,
/// README "Set operations", "CROSS JOIN and FULL OUTER JOIN", "OPENJSON"). Types are read as the server describes them
/// (<c>sys.dm_exec_describe_first_result_set</c>); rows and nulls from values. Each test creates what it needs in a write
/// session, rolled back. Observed on SQL Server 2025 RTM-CU10.
/// </summary>
public class SetJoinObservationTests(SqlServerFixture server)
{
    private Task<CallOutcome> Run(string[] setup, string call, bool errorsAsMessages = false) =>
        ObservedCall.Run(server, setup, call, errorsAsMessages);

    /// <summary>Two small tables: a(1, 2, 3 with a null x) and b(2, 4 with a null y), so a full join is unmatched on both sides.</summary>
    private const string Rows = """
        CREATE TABLE dbo.obs_a (id int NOT NULL PRIMARY KEY, x varchar(5) NULL);
        CREATE TABLE dbo.obs_b (id int NOT NULL PRIMARY KEY, y varchar(5) NULL);
        INSERT INTO dbo.obs_a VALUES (1, 'one'), (2, 'two'), (3, NULL);
        INSERT INTO dbo.obs_b VALUES (2, 'deux'), (4, NULL);
        """;

    private async Task<string> Select(string query) => (await Run([Rows], query)).ToString();

    /// <summary>The server's description of a query's columns: <c>name type</c>, comma-separated.</summary>
    private Task<string> Describe(string select) => FirstResultSet.Describe(server, select);

    // ---- set operations: types -----------------------------------------------------------------------------------------

    [Fact]
    public async Task A_set_operations_columns_are_named_by_its_first_query_and_typed_by_precedence_over_every_query()
    {
        // As COALESCE and CASE (TypeChain.Of): strings are nvarchar if any is, varchar if any varies, of the longest; exact
        // numbers a decimal of every query's integer digits and the largest scale; integers the widest; a date with datetime2
        // that datetime2. INTERSECT and EXCEPT type the same way, though only the left side's values come out.
        Assert.Equal(
            "a bigint, s nvarchar(6), d decimal(10,2), c varchar(4), t int",
            await Describe("""
                SELECT CAST(1 AS int) AS a, 'x' AS s, CAST(1 AS decimal(5,2)) AS d, CAST('c' AS char(4)) AS c, CAST(1 AS tinyint) AS t
                UNION ALL SELECT CAST(2 AS bigint) AS z, N'longer', CAST(1 AS decimal(9,1)), 'ab', 300
                """));
        Assert.Equal(
            "a decimal(12,2), s varchar(3), c varchar(7)",
            await Describe("SELECT CAST(1 AS int) AS a, 'abc' AS s, CAST('c' AS char(4)) AS c INTERSECT SELECT CAST(2 AS decimal(5,2)) AS z, 'x', 'abcdefg'"));
        Assert.Equal(
            "a decimal(12,2), s nvarchar(7)",
            await Describe("SELECT CAST(1 AS int) AS a, 'abc' AS s EXCEPT SELECT CAST(2 AS decimal(5,2)), N'xxxxxxx'"));
        Assert.Equal(
            "c char(6), c2 varchar(4), dt datetime2(3)",
            await Describe("SELECT CAST('c' AS char(4)) AS c, CAST('c' AS char(4)) AS c2, CAST('2020-01-01' AS date) AS dt UNION SELECT CAST('c' AS char(6)), CAST('c' AS varchar(2)), CAST('2020-01-01' AS datetime2(3))"));
    }

    [Fact]
    public async Task A_chain_of_set_operations_is_typed_over_all_its_queries_at_once_so_an_integer_literal_keeps_its_digits()
    {
        // Typed pairwise, 1 UNION ALL 2 would be int, and int beside 2.5 numeric(11,1). The server gives numeric(2,1): every
        // query's type counts at once, an integer literal with its own digits. A parenthesised group is the same.
        Assert.Equal("a numeric(2,1)", await Describe("SELECT 1 AS a UNION ALL SELECT 2 UNION ALL SELECT 2.5"));
        Assert.Equal("a numeric(11,1)", await Describe("SELECT 1 AS a UNION ALL SELECT CAST(2 AS int) UNION ALL SELECT 2.5"));
        Assert.Equal("a numeric(11,1)", await Describe("SELECT 2.5 AS a UNION ALL (SELECT 1 UNION ALL SELECT CAST(2 AS int))"));
        Assert.Equal("a numeric(2,1)", await Describe("SELECT 1 AS a INTERSECT SELECT 2.5"));
    }

    [Fact]
    public async Task A_null_literal_adds_nothing_to_a_set_operations_type_and_alone_is_int()
    {
        Assert.Equal("a varchar(3), b varchar(1), c int", await Describe("SELECT NULL AS a, 'x' AS b, NULL AS c UNION ALL SELECT 'abc', NULL, NULL"));
        Assert.Equal("a tinyint", await Describe("SELECT NULL AS a UNION ALL SELECT CAST(1 AS tinyint)"));
    }

    [Fact]
    public async Task A_fixed_length_string_pads_a_shorter_one_through_a_set_operation()
    {
        // char(4) with a varchar gives varchar(4), and each value is its own; char(4) with char(2) gives char(4), padding 'x'.
        Assert.Equal("c,n: [ab  ],4;[x],1", await Select("SELECT '[' + c + ']' AS c, DATALENGTH(c) AS n FROM (SELECT CAST('ab' AS char(4)) AS c UNION ALL SELECT 'x') AS u ORDER BY n DESC;"));
        Assert.Equal("c: [ab  ];[x   ]", await Select("SELECT '[' + c + ']' AS c FROM (SELECT CAST('ab' AS char(4)) AS c UNION ALL SELECT CAST('x' AS char(2))) AS u ORDER BY c;"));
    }

    // ---- set operations: values and nulls --------------------------------------------------------------------------------

    [Fact]
    public async Task Union_all_keeps_every_row_union_removes_duplicates_and_both_keep_a_null()
    {
        Assert.Equal("x: null;one;one;two", await Select("SELECT x FROM (SELECT x FROM dbo.obs_a UNION ALL SELECT x FROM dbo.obs_a WHERE id = 1) AS u ORDER BY x;"));
        Assert.Equal("x: null;one;two", await Select("SELECT x FROM (SELECT x FROM dbo.obs_a UNION SELECT x FROM dbo.obs_a WHERE id = 1) AS u ORDER BY x;"));

        // A null from the second query only: the column is null though the first query's never is.
        Assert.Equal("id: null;1;2;3", await Select("SELECT id FROM (SELECT id FROM dbo.obs_a UNION ALL SELECT NULL) AS u ORDER BY id;"));
    }

    [Fact]
    public async Task Intersect_and_except_treat_nulls_as_equal_so_intersect_with_a_non_null_side_has_no_null()
    {
        // NULL INTERSECT NULL is a row, NULL EXCEPT NULL none: set operations compare NULLs as equal, unlike =.
        Assert.Equal("n: 1", await Select("SELECT COUNT(*) AS n FROM (SELECT CAST(NULL AS int) AS a INTERSECT SELECT NULL) AS x;"));
        Assert.Equal("n: 0", await Select("SELECT COUNT(*) AS n FROM (SELECT CAST(NULL AS int) AS a EXCEPT SELECT NULL) AS x;"));

        // a.x is null for id 3; b's ids are never null. INTERSECT keeps only rows equal to one of the right side's, so a
        // null left value needs a null on the right; EXCEPT keeps the left side's null when the right has none.
        Assert.Equal("x: two", await Select("SELECT x FROM (SELECT x FROM dbo.obs_a INTERSECT SELECT 'two') AS i;"));
        Assert.Equal("x: null;one", await Select("SELECT x FROM (SELECT x FROM dbo.obs_a EXCEPT SELECT 'two') AS e ORDER BY x;"));
        Assert.Equal("x: null", await Select("SELECT x FROM (SELECT x FROM dbo.obs_a INTERSECT SELECT y FROM dbo.obs_b) AS i;"));
    }

    [Fact]
    public async Task Intersect_and_except_return_the_left_sides_values_and_compare_under_the_collation()
    {
        // Under a case-insensitive collation 'abc' and 'ABC' are equal, and INTERSECT returns the left side's spelling; it
        // ignores trailing spaces and returns the left side's too. So the values are the left side's, whatever the right's.
        Assert.Equal("s: abc", await Select("SELECT s FROM (SELECT 'abc' COLLATE Latin1_General_CI_AS AS s INTERSECT SELECT 'ABC') AS x;"));
        Assert.Equal("s: ABC", await Select("SELECT s FROM (SELECT 'ABC' COLLATE Latin1_General_CI_AS AS s INTERSECT SELECT 'abc') AS x;"));
        Assert.Equal("s: [a ]", await Select("SELECT '[' + s + ']' AS s FROM (SELECT 'a ' AS s INTERSECT SELECT 'a') AS x;"));
        Assert.Equal("s: [a]", await Select("SELECT '[' + s + ']' AS s FROM (SELECT 'a' AS s INTERSECT SELECT 'a ') AS x;"));
    }

    [Fact]
    public async Task Intersect_binds_tighter_than_union_and_except_whatever_the_parser_shows()
    {
        // 1 UNION 2 INTERSECT 3 is 1 UNION (2 INTERSECT 3), one row: left to right it would be none. ScriptDom parses the
        // chain left to right, so the analyser regroups it. With parentheses around the first two queries the same.
        Assert.Equal("a: 1", await Select("SELECT 1 AS a UNION SELECT 2 INTERSECT SELECT 3;"));
        Assert.Equal("a: 1", await Select("(SELECT 1 AS a) UNION (SELECT 2) INTERSECT SELECT 3;"));
        Assert.Equal("a: 1", await Select("SELECT 1 AS a EXCEPT SELECT 2 INTERSECT SELECT 3;"));

        // Parentheses around the union group it first: (1 UNION 2) INTERSECT 3 is empty.
        Assert.Equal("a: ", await Select("(SELECT 1 AS a UNION SELECT 2) INTERSECT SELECT 3;"));
    }

    [Fact]
    public async Task Order_by_and_offset_apply_to_the_whole_set_operation_and_top_to_its_first_query()
    {
        Assert.Equal("a: 2", await Select("SELECT 1 AS a UNION ALL SELECT 2 ORDER BY a DESC OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY;"));
        Assert.Equal("a: 1;2", await Select("SELECT TOP (1) 1 AS a UNION ALL SELECT 2 ORDER BY a;"));

        // Two queries of at most one row each give two rows; INTERSECT and EXCEPT give at most the left side's rows.
        Assert.Equal("id: 1;2", await Select("SELECT id FROM dbo.obs_a WHERE id = 1 UNION SELECT id FROM dbo.obs_a WHERE id = 2 ORDER BY id;"));
    }

    [Fact]
    public async Task A_for_json_subquery_over_a_set_operation_needs_a_derived_table()
    {
        // FOR JSON on a set operation is refused in a subquery, view, inline function or derived table (1086); over a derived
        // table that is one it is fine, and at the top of a statement too.
        Assert.Equal(1086, (await Run([], "SELECT (SELECT 1 AS a UNION ALL SELECT 2 FOR JSON PATH) AS j;")).Error);
        Assert.Equal(
            """j: [{"a":1},{"a":2}]""",
            await Select("SELECT (SELECT d.a FROM (SELECT 1 AS a UNION ALL SELECT 2) AS d ORDER BY d.a FOR JSON PATH) AS j;"));
        Assert.Equal(
            """JSON_F52E2B61-18A1-11d1-B105-00805F49916B: [{"a":1},{"a":2}]""",
            await Select("SELECT 1 AS a UNION ALL SELECT 2 ORDER BY a FOR JSON PATH;"));
    }

    [Fact]
    public async Task Json_through_a_set_operation_is_text_except_json_query()
    {
        // A FOR JSON subquery and a constructor come out of UNION as text, escaped in FOR JSON; json_query stays JSON. Not
        // modelled beyond that, so a set operation over live JSON is unknown.
        Assert.Equal(
            """JSON_F52E2B61-18A1-11d1-B105-00805F49916B: [{"j":"[{\"a\":1}]"},{"j":"[{\"a\":2}]"}]""",
            await Select("SELECT j FROM (SELECT (SELECT 1 AS a FOR JSON PATH) AS j UNION ALL SELECT (SELECT 2 AS a FOR JSON PATH)) AS u FOR JSON PATH;"));
        Assert.Equal(
            """JSON_F52E2B61-18A1-11d1-B105-00805F49916B: [{"j":"{\"a\":1}"},{"j":"{\"a\":2}"}]""",
            await Select("SELECT j FROM (SELECT JSON_OBJECT('a':1) AS j UNION ALL SELECT JSON_OBJECT('a':2)) AS u FOR JSON PATH;"));
        Assert.Equal(
            """JSON_F52E2B61-18A1-11d1-B105-00805F49916B: [{"j":[1]},{"j":[2]}]""",
            await Select("SELECT j FROM (SELECT JSON_QUERY('[1]') AS j UNION ALL SELECT JSON_QUERY('[2]')) AS u FOR JSON PATH;"));
    }

    [Fact]
    public async Task Select_into_from_a_union_does_not_copy_an_identity()
    {
        Assert.Equal(
            "n: 0",
            await Select("""
                CREATE TABLE #src (id int IDENTITY NOT NULL, v int NULL);
                SELECT id INTO #dst FROM #src UNION ALL SELECT id FROM #src;
                SELECT COUNT(*) AS n FROM tempdb.sys.columns WHERE object_id = OBJECT_ID('tempdb..#dst') AND is_identity = 1;
                """));
    }

    // ---- CROSS JOIN and FULL OUTER JOIN ----------------------------------------------------------------------------------

    [Fact]
    public async Task A_cross_join_of_two_one_row_queries_is_one_row_and_with_an_empty_side_none()
    {
        Assert.Equal("n,m: 3,2", await Select("SELECT x.n, y.m FROM (SELECT COUNT(*) AS n FROM dbo.obs_a) AS x CROSS JOIN (SELECT COUNT(*) AS m FROM dbo.obs_b) AS y;"));
        Assert.Equal("id,id: ", await Select("SELECT a.id, b.id FROM dbo.obs_a AS a CROSS JOIN dbo.obs_b AS b WHERE 1 = 0;"));
        Assert.Equal("n: 6", await Select("SELECT COUNT(*) AS n FROM dbo.obs_a AS a CROSS JOIN dbo.obs_b AS b;"));
        Assert.Equal("id,m: ", await Select("SELECT a.id, y.m FROM dbo.obs_a AS a CROSS JOIN (SELECT b.id AS m FROM dbo.obs_b AS b WHERE 1 = 0) AS y;"));
    }

    [Fact]
    public async Task A_full_outer_join_null_extends_both_sides_where_nothing_matches()
    {
        // a 1 and 3 have no b; b 4 has no a. Each unmatched row has every column of the other side NULL, NOT NULL ones too.
        Assert.Equal(
            "a_id,b_id: null,4;1,null;2,2;3,null",
            await Select("SELECT a.id AS a_id, b.id AS b_id FROM dbo.obs_a AS a FULL OUTER JOIN dbo.obs_b AS b ON b.id = a.id ORDER BY a.id, b.id;"));
    }

    [Fact]
    public async Task A_full_outer_joins_on_proves_nothing_of_either_side()
    {
        // ON b.y IS NOT NULL holds of matched rows only: b 4, whose y is null, comes back unmatched, its y null; so does a 3's x.
        Assert.Equal(
            "x,y: null,null;null,null;one,null;two,deux",
            await Select("SELECT a.x, b.y FROM dbo.obs_a AS a FULL JOIN dbo.obs_b AS b ON b.id = a.id AND b.y IS NOT NULL AND a.x IS NOT NULL ORDER BY a.x, b.y;"));
    }

    [Fact]
    public async Task A_where_on_one_side_of_a_full_join_drops_that_sides_unmatched_rows_only()
    {
        // WHERE a.id > 0 drops b 4's row, which has no a; a 1 and 3 stay with b NULL.
        Assert.Equal(
            "a_id,b_id: 1,null;2,2;3,null",
            await Select("SELECT a.id AS a_id, b.id AS b_id FROM dbo.obs_a AS a FULL JOIN dbo.obs_b AS b ON b.id = a.id WHERE a.id > 0 ORDER BY a.id;"));
    }

    [Fact]
    public async Task A_full_join_of_two_queries_of_at_most_one_row_can_return_two()
    {
        // Each side is one row, and they do not match: two rows. So neither side's bound bounds the join, unlike a left join.
        Assert.Equal(
            "a_id,b_id: null,4;1,null",
            await Select("SELECT a.id AS a_id, b.id AS b_id FROM (SELECT id FROM dbo.obs_a WHERE id = 1) AS a FULL JOIN (SELECT id FROM dbo.obs_b WHERE id = 4) AS b ON b.id = a.id ORDER BY a.id;"));

        // And a key tested IS NULL matches every row the other side null-extends.
        Assert.Equal(
            "a_id,b_id: 1,null;3,null",
            await Select("SELECT a.id AS a_id, b.id AS b_id FROM dbo.obs_a AS a FULL JOIN dbo.obs_b AS b ON b.id = a.id WHERE b.id IS NULL ORDER BY a.id;"));
    }

    // ---- OPENJSON -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Openjson_without_with_gives_key_value_and_type()
    {
        Assert.Equal("key nvarchar(4000), value nvarchar(max), type tinyint", await Describe("SELECT * FROM OPENJSON(N'[1]')"));

        // value is NULL for a JSON null, and type is 0 to 5: null, string, number, true/false, array, object.
        Assert.Equal(
            "key,value,type: a,null,0;b,1,2;c,s,1;d,true,3;e,[1],4;f,{\"x\":1},5",
            await Select("""SELECT [key], value, type FROM OPENJSON(N'{"a":null,"b":1,"c":"s","d":true,"e":[1],"f":{"x":1}}');"""));
        Assert.Equal("n: 0", await Select("SELECT COUNT(*) AS n FROM OPENJSON(NULL);"));

        // value nests in FOR JSON as escaped text.
        Assert.Equal(
            """JSON_F52E2B61-18A1-11d1-B105-00805F49916B: [{"value":"{\"x\":1}"}]""",
            await Select("""SELECT value FROM OPENJSON(N'[{"x":1}]') FOR JSON PATH;"""));
    }

    [Fact]
    public async Task Openjson_with_columns_are_of_their_declared_types_and_null_for_a_missing_path_or_a_json_null()
    {
        Assert.Equal(
            "a int, s varchar(3), j nvarchar(max)",
            await Describe("SELECT * FROM OPENJSON(N'[1]') WITH (a int '$.a', s varchar(3) 'strict $.s', j nvarchar(max) '$.j' AS JSON)"));

        // A lax path that is missing gives NULL; strict gives NULL for a JSON null too. A longer string is cut to its length.
        Assert.Equal(
            "a,s: 1,abc;null,x;null,null",
            await Select("""SELECT a, s FROM OPENJSON(N'[{"a":1,"s":"abcdef"},{"s":"x"},{"a":null,"s":null}]') WITH (a int '$.a', s varchar(3) 'strict $.s');"""));
    }

    [Fact]
    public async Task Openjson_with_a_strict_path_that_is_missing_is_an_error_not_a_null()
    {
        Assert.Equal(13608, (await Run([], """SELECT a FROM OPENJSON(N'[{"b":1}]') WITH (a int 'strict $.a');""")).Error);
        Assert.Equal(13624, (await Run([], """SELECT j FROM OPENJSON(N'[{"j":5}]') WITH (j nvarchar(max) 'strict $.j' AS JSON);""")).Error);
        Assert.Equal(13608, (await Run([], """SELECT COUNT(*) FROM OPENJSON(N'{"a":1}', 'strict $.zz');""")).Error);

        // A lax row path that is missing gives no rows.
        Assert.Equal("n: 0", await Select("""SELECT COUNT(*) AS n FROM OPENJSON(N'{"a":1}', '$.zz');"""));
    }

    [Fact]
    public async Task Openjson_with_converts_as_cast_does_a_failure_an_error_and_an_overflow_null_under_the_callers_options()
    {
        Assert.Equal(245, (await Run([], """SELECT a FROM OPENJSON(N'[{"a":"abc"}]') WITH (a int '$.a');""")).Error);
        Assert.Equal(244, (await Run([], """SELECT a FROM OPENJSON(N'[{"a":300}]') WITH (a tinyint '$.a');""")).Error);
        Assert.Equal(
            "a: null",
            await Select("""SET ANSI_WARNINGS OFF; SET ARITHABORT OFF; SELECT a FROM OPENJSON(N'[{"a":300}]') WITH (a tinyint '$.a');"""));

        // An object or array under a scalar column is NULL in lax mode.
        Assert.Equal("a: null", await Select("""SELECT a FROM OPENJSON(N'[{"a":{"x":1}}]') WITH (a int '$.a');"""));
    }

    [Fact]
    public async Task Openjson_as_json_is_nvarchar_max_holding_an_object_or_array_that_nests_as_json()
    {
        // A scalar, a missing path or a JSON null give NULL; an object or array its text, which FOR JSON nests as JSON.
        Assert.Equal(
            "j: {\"x\":1};null;null;null",
            await Select("""SELECT j FROM OPENJSON(N'[{"j":{"x":1}},{"j":5},{"k":1},{"j":null}]') WITH (j nvarchar(max) '$.j' AS JSON);"""));
        Assert.Equal(
            """JSON_F52E2B61-18A1-11d1-B105-00805F49916B: [{"j":{"x":1}}]""",
            await Select("""SELECT j FROM OPENJSON(N'[{"j":{"x":1}}]') WITH (j nvarchar(max) '$.j' AS JSON) FOR JSON PATH;"""));

        // AS JSON needs nvarchar(max) (13618).
        Assert.Equal(13618, (await Run([], """SELECT j FROM OPENJSON(N'[{"j":{"x":1}}]') WITH (j nvarchar(100) '$.j' AS JSON);""")).Error);
    }
}
