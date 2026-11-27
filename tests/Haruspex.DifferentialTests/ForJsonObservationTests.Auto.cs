using Microsoft.Data.SqlClient;

namespace Haruspex.DifferentialTests;

/// <summary>
/// What <c>FOR JSON AUTO</c> and <c>ROOT</c> write. Microsoft's page describes <c>AUTO</c>'s nesting only loosely, so every rule
/// the inferencer follows (<c>Typer.ForJsonAuto.cs</c>) is one of these.
/// </summary>
public partial class ForJsonObservationTests
{
    /// <summary>Orders, their lines and their notes, in temporary tables: order 3 has no lines, and only order 1 a note.</summary>
    private const string Orders = """
        CREATE TABLE #o (id int NOT NULL, placed_on date NOT NULL, note nvarchar(10) NULL);
        CREATE TABLE #l (id int NOT NULL, order_id int NOT NULL, sku varchar(5) NOT NULL);
        CREATE TABLE #n (id int NOT NULL, order_id int NOT NULL, body nvarchar(5) NULL);
        INSERT INTO #o VALUES (1, '2026-01-01', N'first'), (2, '2026-01-02', NULL), (3, '2026-01-03', NULL);
        INSERT INTO #l VALUES (10, 1, 'A'), (11, 1, 'B'), (12, 2, 'C');
        INSERT INTO #n VALUES (20, 1, N'x');

        """;

    private Task<string> Auto(string sql) => Json(Orders + sql);

    // ---- nesting --------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task For_json_auto_nests_a_second_tables_columns_in_an_array_keyed_by_its_alias() =>
        Assert.Equal(
            """[{"id":1,"placed_on":"2026-01-01","l":[{"sku":"A"},{"sku":"B"}]},{"id":2,"placed_on":"2026-01-02","l":[{"sku":"C"}]}]""",
            await Auto("SELECT o.id, o.placed_on, l.sku FROM #o AS o JOIN #l AS l ON l.order_id = o.id ORDER BY o.id, l.id FOR JSON AUTO;"));

    [Theory]
    // Each table nests under the one before it in the select list, not under the table it is joined to.
    [InlineData("o.id, n.body, l.sku", """[{"id":1,"n":[{"body":"x","l":[{"sku":"A"},{"sku":"B"}]}]}]""")]
    [InlineData("o.id, l.sku, n.body", """[{"id":1,"l":[{"sku":"A","n":[{"body":"x"}]},{"sku":"B","n":[{"body":"x"}]}]}]""")]
    // The first table selected is the outermost, whatever the FROM's order.
    [InlineData("l.sku, o.id", """[{"sku":"A","o":[{"id":1}]},{"sku":"B","o":[{"id":1}]}]""")]
    public async Task For_json_auto_nests_tables_in_a_chain_in_the_order_their_first_columns_are_selected(string columns, string expected) =>
        Assert.Equal(expected, await Auto($"SELECT {columns} FROM #o AS o JOIN #l AS l ON l.order_id = o.id JOIN #n AS n ON n.order_id = o.id ORDER BY o.id, l.id FOR JSON AUTO;"));

    [Fact]
    public async Task For_json_auto_gathers_a_tables_columns_wherever_they_stand_and_writes_the_nested_array_last() =>
        Assert.Equal(
            """[{"id":2,"placed_on":"2026-01-02","l":[{"sku":"C","oid":2}]}]""",
            await Auto("SELECT o.id, l.sku, o.placed_on, l.order_id AS oid FROM #o AS o JOIN #l AS l ON l.order_id = o.id WHERE o.id = 2 FOR JSON AUTO;"));

    [Fact]
    public async Task For_json_auto_gathers_a_tables_columns_into_its_own_object_at_any_depth() =>
        Assert.Equal(
            """[{"id":2,"placed_on":"2026-01-02","l":[{"sku":"C","oid":2,"n":[{"body":"x"}]}]}]""",
            await Auto("SELECT o.id, l.sku, n.body, o.placed_on, l.order_id AS oid FROM #o AS o JOIN #l AS l ON l.order_id = o.id CROSS JOIN #n AS n WHERE o.id = 2 FOR JSON AUTO;"));

    [Fact]
    public async Task Two_aliases_of_one_table_are_two_levels() =>
        Assert.Equal(
            """[{"id":1,"b":[{"bid":1}]}]""",
            await Auto("SELECT a.id, b.id AS bid FROM #o AS a JOIN #o AS b ON b.id = a.id WHERE a.id = 1 FOR JSON AUTO;"));

    [Fact]
    public async Task For_json_auto_starts_a_new_object_for_a_parent_whose_rows_are_not_adjacent() =>
        // Order 1's two lines are apart, so order 1 is written twice, each with the lines next to it.
        Assert.Equal(
            """[{"id":1,"l":[{"sku":"A"}]},{"id":2,"l":[{"sku":"C"}]},{"id":1,"l":[{"sku":"B"}]}]""",
            await Auto("SELECT o.id, l.sku FROM #o AS o JOIN #l AS l ON l.order_id = o.id ORDER BY CASE l.id WHEN 10 THEN 1 WHEN 12 THEN 2 ELSE 3 END FOR JSON AUTO;"));

    // ---- rows that don't match ------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("FOR JSON AUTO", """[{"id":2,"l":[{"sku":"C"}]},{"id":3,"l":[{}]}]""")]
    [InlineData("FOR JSON AUTO, INCLUDE_NULL_VALUES", """[{"id":2,"note":null,"l":[{"sku":"C"}]},{"id":3,"note":null,"l":[{"sku":null}]}]""")]
    public async Task An_unmatched_child_is_one_object_with_its_keys_left_out_or_with_include_null_values_null(string clause, string expected) =>
        Assert.Equal(expected, await Auto($"SELECT o.id, o.note, l.sku FROM #o AS o LEFT JOIN #l AS l ON l.order_id = o.id WHERE o.id > 1 ORDER BY o.id {clause};"));

    [Fact]
    public async Task An_unmatched_parent_is_an_object_with_its_keys_left_out() =>
        Assert.Equal(
            """[{"l":[{"sku":"A"},{"sku":"B"}]},{"id":2,"l":[{"sku":"C"}]}]""",
            await Auto("SELECT o.id, l.sku FROM #o AS o RIGHT JOIN #l AS l ON l.order_id = o.id AND o.id = 2 ORDER BY l.id FOR JSON AUTO;"));

    // ---- columns of no table --------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("1 AS e, o.id, l.sku", """[{"e":1,"id":2,"l":[{"sku":"C"}]}]""")]
    [InlineData("o.id, 1 AS e, l.sku", """[{"id":2,"e":1,"l":[{"sku":"C"}]}]""")]
    [InlineData("o.id, l.sku, 1 AS e", """[{"id":2,"l":[{"sku":"C","e":1}]}]""")]
    // The deepest object opened so far, not the table of the column before it.
    [InlineData("o.id, l.sku, o.placed_on, 1 AS e", """[{"id":2,"placed_on":"2026-01-02","l":[{"sku":"C","e":1}]}]""")]
    // A function of a column, a conversion, COLLATE and CASE are columns of no table, so they open no object.
    [InlineData("o.id, upper(l.sku) AS u", """[{"id":2,"u":"C"}]""")]
    [InlineData("upper(l.sku) AS u, o.id", """[{"u":"C","id":2}]""")]
    [InlineData("o.id, CAST(l.id AS bigint) AS lid", """[{"id":2,"lid":12}]""")]
    [InlineData("o.id, l.sku COLLATE Latin1_General_BIN AS s", """[{"id":2,"s":"C"}]""")]
    [InlineData("o.id, CASE WHEN l.id > 0 THEN 1 END AS big", """[{"id":2,"big":1}]""")]
    // A column in parentheses is still the table's.
    [InlineData("o.id, (l.sku) AS s", """[{"id":2,"l":[{"s":"C"}]}]""")]
    [InlineData("o.id, (l.sku)", """[{"id":2,"l":[{"sku":"C"}]}]""")]
    public async Task A_column_of_no_table_joins_the_deepest_object_opened_before_it(string columns, string expected) =>
        Assert.Equal(expected, await Auto($"SELECT {columns} FROM #o AS o JOIN #l AS l ON l.order_id = o.id WHERE o.id = 2 FOR JSON AUTO;"));

    [Fact]
    public async Task A_column_of_the_enclosing_query_is_a_column_of_no_table() =>
        Assert.Equal(
            """{"j":[{"sku":"C","id":2}]}""",
            await Auto("SELECT (SELECT l.sku, o.id FROM #l AS l WHERE l.order_id = o.id FOR JSON AUTO) AS j FROM #o AS o WHERE o.id = 2 FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;"));

    // ---- which sources are tables ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("CROSS APPLY (SELECT l.sku FROM #l AS l WHERE l.order_id = o.id) AS x", "x.sku", """[{"id":2,"x":[{"sku":"C"}]}]""")]
    [InlineData("CROSS JOIN (VALUES (5)) AS x (sku)", "x.sku", """[{"id":2,"x":[{"sku":5}]}]""")]
    [InlineData("CROSS APPLY (SELECT o.id + 1 AS sku) AS x", "x.sku", """[{"id":2,"x":[{"sku":3}]}]""")]
    // Built-in rowset functions are not.
    [InlineData("CROSS APPLY OPENJSON(N'[{\"v\":1}]') WITH (v int) AS x", "x.v", """[{"id":2,"v":1}]""")]
    [InlineData("CROSS APPLY OPENJSON(N'[1]') AS x", "x.value", """[{"id":2,"value":"1"}]""")]
    [InlineData("CROSS APPLY STRING_SPLIT(N'a', N',') AS x", "x.value", """[{"id":2,"value":"a"}]""")]
    [InlineData("CROSS APPLY GENERATE_SERIES(7, 7) AS x", "x.value", """[{"id":2,"value":7}]""")]
    public async Task Derived_tables_and_values_are_tables_to_for_json_auto_and_built_in_rowset_functions_are_not(string source, string column, string expected) =>
        Assert.Equal(expected, await Auto($"SELECT o.id, {column} FROM #o AS o {source} WHERE o.id = 2 FOR JSON AUTO;"));

    [Fact]
    public async Task A_cte_and_a_table_variable_are_tables_to_for_json_auto() =>
        Assert.Equal(
            """[{"id":2,"c":[{"sku":"C","@v":[{"k":5}]}]}]""",
            await Auto("""
                DECLARE @v TABLE (k int NOT NULL);
                INSERT INTO @v VALUES (5);
                WITH c AS (SELECT l.order_id, l.sku FROM #l AS l)
                SELECT o.id, c.sku, [@v].k FROM #o AS o JOIN c ON c.order_id = o.id CROSS JOIN @v WHERE o.id = 2 FOR JSON AUTO;
                """));

    [Fact]
    public async Task Openxml_and_a_system_view_are_tables_to_for_json_auto() =>
        Assert.Equal(
            """[{"id":2,"x":[{"v":1,"t":[{"tid":56}]}]}]""",
            await Auto("""
                DECLARE @h int;
                EXEC sys.sp_xml_preparedocument @h OUTPUT, N'<r><a v="1"/></r>';
                SELECT o.id, x.v, t.system_type_id AS tid
                FROM #o AS o CROSS JOIN OPENXML(@h, N'/r/a') WITH (v int) AS x JOIN sys.types AS t ON t.name = N'int'
                WHERE o.id = 2 FOR JSON AUTO;
                EXEC sys.sp_xml_removedocument @h;
                """));

    [Fact]
    public async Task An_xml_nodes_source_is_a_table_to_for_json_auto_whose_column_is_never_bare()
    {
        // Accepted with no other source, as OPENJSON is not; but its column can't stand alone in a select list (493), so
        // only an expression of it, which is a column of no table, ever reaches the JSON.
        const string Xml = "DECLARE @x xml = N'<a>1</a>';\n";
        Assert.Equal("""[{"v":1}]""", await Auto(Xml + "SELECT n.c.value('.', 'int') AS v FROM @x.nodes('/a') AS n (c) FOR JSON AUTO;"));
        Assert.Equal("""[{"id":2,"v":1}]""", await Auto(Xml + "SELECT o.id, n.c.value('.', 'int') AS v FROM #o AS o CROSS APPLY @x.nodes('/a') AS n (c) WHERE o.id = 2 FOR JSON AUTO;"));
        var e = await Assert.ThrowsAsync<SqlException>(() => Auto(Xml + "SELECT o.id, n.c FROM #o AS o CROSS APPLY @x.nodes('/a') AS n (c) FOR JSON AUTO;"));
        Assert.Equal(493, e.Number);
    }

    [Theory]
    [InlineData("SELECT 1 AS a FOR JSON AUTO;")]
    [InlineData("SELECT x.v FROM OPENJSON(N'[{\"v\":1}]') WITH (v int) AS x FOR JSON AUTO;")]
    [InlineData("SELECT x.value FROM STRING_SPLIT(N'a', N',') AS x FOR JSON AUTO;")]
    [InlineData("SELECT x.value FROM GENERATE_SERIES(1, 2) AS x FOR JSON AUTO;")]
    public async Task For_json_auto_refuses_a_query_whose_from_has_no_table(string sql)
    {
        var e = await Assert.ThrowsAsync<SqlException>(() => Json(sql));
        Assert.Equal(13600, e.Number);
    }

    [Fact]
    public async Task A_from_with_a_table_is_enough_though_no_column_reads_it() =>
        Assert.Equal("""[{"a":1},{"a":1},{"a":1}]""", await Auto("SELECT 1 AS a FROM #o AS o FOR JSON AUTO;"));

    [Fact]
    public async Task A_source_without_an_alias_is_keyed_by_its_name_as_the_from_clause_writes_it()
    {
        await server.RequireAsync();
        var token = TestContext.Current.CancellationToken;
        await using var session = await server.WriteAsync(token);
        foreach (var create in new[]
        {
            Orders,
            "CREATE TABLE dbo.obs_auto_tags (order_id int NOT NULL, tag varchar(5) NOT NULL); INSERT INTO dbo.obs_auto_tags VALUES (2, 't');",
            "CREATE VIEW dbo.obs_auto_view AS SELECT t.order_id, t.tag FROM dbo.obs_auto_tags AS t;",
            "CREATE FUNCTION dbo.obs_auto_fn (@o int) RETURNS TABLE AS RETURN SELECT t.tag FROM dbo.obs_auto_tags AS t WHERE t.order_id = @o;",
            "CREATE FUNCTION dbo.obs_auto_ms (@o int) RETURNS @r TABLE (tag varchar(5)) AS BEGIN INSERT INTO @r SELECT t.tag FROM dbo.obs_auto_tags AS t WHERE t.order_id = @o; RETURN; END;",
        })
        {
            await using var command = session.Command(create);
            await command.ExecuteNonQueryAsync(token);
        }

        async Task<string> Run(string sql)
        {
            await using var command = session.Command(sql);
            await using var reader = await command.ExecuteReaderAsync(token);
            var text = new System.Text.StringBuilder();
            while (await reader.ReadAsync(token))
                text.Append(reader.GetString(0));
            return text.ToString();
        }

        // The name's parts as written, joined with dots, without brackets and in the case the FROM writes them, whatever the
        // select list writes; a view's and a function's as a table's, and an alias wins.
        Assert.Equal("""[{"id":2,"obs_auto_tags":[{"tag":"t"}]}]""", await Run("SELECT o.id, obs_auto_tags.tag FROM #o AS o JOIN obs_auto_tags ON obs_auto_tags.order_id = o.id FOR JSON AUTO;"));
        Assert.Equal("""[{"id":2,"dbo.OBS_AUTO_TAGS":[{"tag":"t"}]}]""", await Run("SELECT o.id, obs_auto_tags.tag FROM #o AS o JOIN [dbo].[OBS_AUTO_TAGS] ON obs_auto_tags.order_id = o.id FOR JSON AUTO;"));
        Assert.Equal("""[{"id":2,"dbo.obs_auto_view":[{"tag":"t"}]}]""", await Run("SELECT o.id, obs_auto_view.tag FROM #o AS o JOIN dbo.obs_auto_view ON obs_auto_view.order_id = o.id FOR JSON AUTO;"));
        Assert.Equal("""[{"id":2,"dbo.obs_auto_fn":[{"tag":"t"}]}]""", await Run("SELECT o.id, obs_auto_fn.tag FROM #o AS o CROSS APPLY dbo.obs_auto_fn(o.id) WHERE o.id = 2 FOR JSON AUTO;"));
        Assert.Equal("""[{"id":2,"m":[{"tag":"t"}]}]""", await Run("SELECT o.id, m.tag FROM #o AS o CROSS APPLY dbo.obs_auto_ms(o.id) AS m WHERE o.id = 2 FOR JSON AUTO;"));
        Assert.Equal("""[{"id":2,"#l":[{"sku":"C"}]}]""", await Run("SELECT o.id, #l.sku FROM #o AS o JOIN #l ON #l.order_id = o.id WHERE o.id = 2 FOR JSON AUTO;"));
    }

    // ---- keys -----------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task For_json_auto_writes_a_dotted_alias_as_one_key() =>
        Assert.Equal(
            """[{"a.b":2,"l":[{"c.d":"C"}]}]""",
            await Auto("SELECT o.id AS [a.b], l.sku AS [c.d] FROM #o AS o JOIN #l AS l ON l.order_id = o.id WHERE o.id = 2 FOR JSON AUTO;"));

    [Theory]
    [InlineData("o.id, o.id", """[{"id":2,"id":2}]""")]
    [InlineData("o.id AS l, l.sku", """[{"l":2,"l":[{"sku":"C"}]}]""")]
    // Keys that differ only in case are two keys, as under PATH.
    [InlineData("o.id, o.id AS ID", """[{"id":2,"ID":2}]""")]
    public async Task For_json_auto_writes_a_repeated_key_twice(string columns, string expected) =>
        Assert.Equal(expected, await Auto($"SELECT {columns} FROM #o AS o JOIN #l AS l ON l.order_id = o.id WHERE o.id = 2 FOR JSON AUTO;"));

    [Fact]
    public async Task Without_array_wrapper_leaves_out_only_the_outermost_array() =>
        Assert.Equal(
            """{"id":1,"l":[{"sku":"A"},{"sku":"B"}]}""",
            await Auto("SELECT o.id, l.sku FROM #o AS o JOIN #l AS l ON l.order_id = o.id WHERE o.id = 1 ORDER BY l.id FOR JSON AUTO, WITHOUT_ARRAY_WRAPPER;"));

    [Theory]
    [InlineData("FOR JSON AUTO")]
    [InlineData("FOR JSON AUTO, ROOT('r')")]
    public async Task A_top_level_for_json_auto_query_with_no_rows_returns_a_result_set_with_no_row(string clause) =>
        Assert.Empty(Assert.Single(await Query($"{Orders}SELECT o.id, l.sku FROM #o AS o JOIN #l AS l ON l.order_id = o.id WHERE 1 = 0 {clause};")));

    [Theory]
    [InlineData("", """[{"k":1,"j":[{"id":1,"l":[{"sku":"A"},{"sku":"B"}]}]}]""")]
    [InlineData(", WITHOUT_ARRAY_WRAPPER", """[{"k":1,"j":"{\"id\":1,\"l\":[{\"sku\":\"A\"},{\"sku\":\"B\"}]}"}]""")]
    public async Task A_for_json_auto_subquery_nests_as_json_with_its_wrapper_and_as_text_without(string option, string expected) =>
        Assert.Equal(expected, await Auto($"SELECT 1 AS k, (SELECT o.id, l.sku FROM #o AS o JOIN #l AS l ON l.order_id = o.id WHERE o.id = 1 ORDER BY l.id FOR JSON AUTO{option}) AS j FOR JSON PATH;"));

    [Fact]
    public async Task A_for_json_auto_subquery_with_no_rows_is_null() =>
        Assert.Null(Assert.Single(Assert.Single(await Query($"{Orders}SELECT (SELECT o.id, l.sku FROM #o AS o JOIN #l AS l ON l.order_id = o.id WHERE 1 = 0 FOR JSON AUTO) AS s;"))));

    // ---- ROOT -----------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("FOR JSON PATH, ROOT", """{"root":[{"id":1},{"id":2}]}""")]
    [InlineData("FOR JSON PATH, ROOT('orders')", """{"orders":[{"id":1},{"id":2}]}""")]
    [InlineData("FOR JSON PATH, ROOT(N'a.b')", """{"a.b":[{"id":1},{"id":2}]}""")]
    [InlineData("FOR JSON PATH, ROOT('')", """{"":[{"id":1},{"id":2}]}""")]
    [InlineData("FOR JSON AUTO, ROOT('orders')", """{"orders":[{"id":1},{"id":2}]}""")]
    [InlineData("FOR JSON PATH, ROOT('r'), INCLUDE_NULL_VALUES", """{"r":[{"id":1},{"id":2}]}""")]
    public async Task Root_wraps_the_array_in_an_object_whose_one_key_is_root_or_the_name_given(string clause, string expected) =>
        Assert.Equal(expected, await Auto($"SELECT o.id FROM #o AS o WHERE o.id < 3 ORDER BY o.id {clause};"));

    [Fact]
    public async Task A_root_over_one_aggregate_row_is_always_written() =>
        Assert.Equal("""{"r":[{"n":0}]}""", await Auto("SELECT COUNT(*) AS n FROM #o AS o WHERE 1 = 0 FOR JSON PATH, ROOT('r');"));

    [Fact]
    public async Task A_root_subquery_nests_as_json_and_is_null_over_no_rows() =>
        Assert.Equal(
            """[{"j":{"r":[{"id":2}]}}]""",
            await Auto("SELECT (SELECT o.id FROM #o AS o WHERE o.id = 2 FOR JSON PATH, ROOT('r')) AS j, (SELECT o.id FROM #o AS o WHERE 1 = 0 FOR JSON AUTO, ROOT('r')) AS none FOR JSON PATH;"));

    [Theory]
    [InlineData("FOR JSON PATH, ROOT('r'), WITHOUT_ARRAY_WRAPPER")]
    [InlineData("FOR JSON AUTO, ROOT, WITHOUT_ARRAY_WRAPPER")]
    public async Task Root_with_without_array_wrapper_is_refused(string clause)
    {
        var e = await Assert.ThrowsAsync<SqlException>(() => Auto($"SELECT o.id FROM #o AS o {clause};"));
        Assert.Equal(13620, e.Number);
    }
}
