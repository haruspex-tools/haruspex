using Haruspex.DifferentialTests.Server;

namespace Haruspex.DifferentialTests;

/// <summary>
/// What SQL Server does with <c>CROSS APPLY</c>, <c>OUTER APPLY</c>, <c>VALUES</c> lists and views, observed and kept as
/// tests: the rules slice 6e rests on (Haruspex.SqlServer.Inference, README "APPLY" and "Views"). Which outer rows an APPLY
/// keeps, what an applied aggregate gives, what an applied query can see, what type a VALUES column has, and what a view
/// binds when it is created. Each test creates what it needs in a write session, rolled back. Observed on SQL Server 2025
/// RTM-CU10.
/// </summary>
public class ApplyViewObservationTests(SqlServerFixture server)
{
    private Task<CallOutcome> Run(string[] setup, string call, bool errorsAsMessages = false) =>
        ObservedCall.Run(server, setup, call, errorsAsMessages);

    /// <summary>Three outer rows: 1 with two lines (one quantity null), 2 with none and a null x, 3 with one line.</summary>
    private const string Rows = """
        CREATE TABLE dbo.obs_outer (id int NOT NULL PRIMARY KEY, x int NULL);
        CREATE TABLE dbo.obs_line (id int NOT NULL PRIMARY KEY, outer_id int NOT NULL, q int NULL);
        INSERT INTO dbo.obs_outer VALUES (1, 10), (2, NULL), (3, 30);
        INSERT INTO dbo.obs_line VALUES (1, 1, 5), (2, 1, NULL), (3, 3, 7);
        """;

    private async Task<string> Select(string query) => (await Run([Rows], query)).ToString();

    // ---- APPLY -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Cross_apply_keeps_only_outer_rows_with_a_right_row_and_outer_apply_fills_the_others_with_null()
    {
        // Outer row 2 has no line: CROSS APPLY drops it, OUTER APPLY keeps it with every right-side column NULL.
        Assert.Equal(
            "id,q: 1,5;1,null;3,7",
            await Select("SELECT o.id, l.q FROM dbo.obs_outer AS o CROSS APPLY (SELECT x.q FROM dbo.obs_line AS x WHERE x.outer_id = o.id) AS l ORDER BY o.id, l.q DESC;"));
        Assert.Equal(
            "id,line_id: 1,1;1,2;2,null;3,3",
            await Select("SELECT o.id, l.line_id FROM dbo.obs_outer AS o OUTER APPLY (SELECT x.id AS line_id FROM dbo.obs_line AS x WHERE x.outer_id = o.id) AS l ORDER BY o.id, l.line_id;"));
    }

    [Fact]
    public async Task An_ungrouped_aggregate_applied_gives_one_row_for_every_outer_row()
    {
        // Without GROUP BY an aggregate query returns one row even over no rows, so neither APPLY drops or null-extends an
        // outer row: COUNT is 0 and MAX is NULL (an empty aggregate, not a missing row) for outer row 2.
        const string Right = "(SELECT COUNT(*) AS n, MAX(x.q) AS m FROM dbo.obs_line AS x WHERE x.outer_id = o.id) AS a";
        Assert.Equal("id,n,m: 1,2,5;2,0,null;3,1,7", await Select($"SELECT o.id, a.n, a.m FROM dbo.obs_outer AS o CROSS APPLY {Right} ORDER BY o.id;"));
        Assert.Equal("id,n,m: 1,2,5;2,0,null;3,1,7", await Select($"SELECT o.id, a.n, a.m FROM dbo.obs_outer AS o OUTER APPLY {Right} ORDER BY o.id;"));
    }

    [Fact]
    public async Task A_grouped_aggregate_applied_gives_no_row_for_an_outer_row_without_rows()
    {
        // With GROUP BY, even GROUP BY (), no rows means no group: CROSS APPLY drops outer row 2, OUTER APPLY null-extends it.
        const string Right = "(SELECT COUNT(*) AS n FROM dbo.obs_line AS x WHERE x.outer_id = o.id GROUP BY ()) AS a";
        Assert.Equal("id,n: 1,2;3,1", await Select($"SELECT o.id, a.n FROM dbo.obs_outer AS o CROSS APPLY {Right} ORDER BY o.id;"));
        Assert.Equal("id,n: 1,2;2,null;3,1", await Select($"SELECT o.id, a.n FROM dbo.obs_outer AS o OUTER APPLY {Right} ORDER BY o.id;"));
    }

    [Fact]
    public async Task An_outer_column_read_in_an_applied_aggregate_is_not_filtered_by_its_where()
    {
        // The applied query's WHERE rejects outer row 2 (x is NULL), but the aggregate still returns its one row, which
        // reads o.x as it is: NULL. So a WHERE inside the applied query proves nothing of the outer columns it reads.
        Assert.Equal(
            "id,ox,n: 1,10,2;2,null,0;3,30,1",
            await Select("SELECT o.id, a.ox, a.n FROM dbo.obs_outer AS o CROSS APPLY (SELECT o.x AS ox, COUNT(*) AS n FROM dbo.obs_line AS x WHERE o.x IS NOT NULL AND x.outer_id = o.id) AS a ORDER BY o.id;"));
    }

    [Fact]
    public async Task A_for_json_subquery_applied_without_from_is_one_row_whose_json_is_null_when_there_are_no_rows()
    {
        // The nested-payload idiom: the applied query has no FROM, so it is one row for every outer row under either
        // APPLY; its FOR JSON subquery is NULL for an outer row without lines.
        Assert.Equal(
            """id,lines: 1,[{"id":1,"q":5},{"id":2}];2,null;3,[{"id":3,"q":7}]""",
            await Select("SELECT o.id, j.lines FROM dbo.obs_outer AS o OUTER APPLY (SELECT JSON_QUERY((SELECT x.id, x.q FROM dbo.obs_line AS x WHERE x.outer_id = o.id ORDER BY x.id FOR JSON PATH)) AS lines) AS j ORDER BY o.id;"));
        Assert.Equal(
            """id,lines: 1,[{"id":1,"q":5},{"id":2}];2,null;3,[{"id":3,"q":7}]""",
            await Select("SELECT o.id, j.lines FROM dbo.obs_outer AS o CROSS APPLY (SELECT JSON_QUERY((SELECT x.id, x.q FROM dbo.obs_line AS x WHERE x.outer_id = o.id ORDER BY x.id FOR JSON PATH)) AS lines) AS j ORDER BY o.id;"));
    }

    [Fact]
    public async Task Top_1_applied_gives_at_most_one_row_for_every_outer_row()
    {
        Assert.Equal(
            "id,q: 1,5;2,null;3,7",
            await Select("SELECT o.id, t.q FROM dbo.obs_outer AS o OUTER APPLY (SELECT TOP (1) x.q FROM dbo.obs_line AS x WHERE x.outer_id = o.id ORDER BY x.id) AS t ORDER BY o.id;"));
    }

    [Fact]
    public async Task An_applied_query_sees_its_left_input_only()
    {
        // The right side of an APPLY may read the columns of the table expression on its left, joins included, and an
        // enclosing query's; not a source listed earlier with a comma, which is not part of its left input (4104).
        Assert.Equal(
            "n: 3",
            await Select("SELECT COUNT(*) AS n FROM dbo.obs_outer AS a JOIN dbo.obs_line AS b ON b.outer_id = a.id CROSS APPLY (SELECT a.x AS ax, b.q AS bq) AS z;"));
        Assert.Equal(4104, (await Run([Rows], "SELECT COUNT(*) AS n FROM dbo.obs_outer AS a, dbo.obs_line AS b CROSS APPLY (SELECT a.x AS ax) AS z;")).Error);

        // A derived table nested inside the applied query sees the left input too.
        Assert.Equal("n: 3", await Select("SELECT COUNT(*) AS n FROM dbo.obs_outer AS a CROSS APPLY (SELECT d.ax FROM (SELECT a.x AS ax) AS d) AS z;"));
    }

    // ---- VALUES ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_values_list_gives_one_row_for_each_of_its_rows_for_every_outer_row()
    {
        // An unpivot: two rows for each outer row, under CROSS APPLY too, with the outer column's NULL kept as it is.
        Assert.Equal(
            "id,v: 1,1;1,10;2,null;2,2;3,3;3,30",
            await Select("SELECT o.id, v.v FROM dbo.obs_outer AS o CROSS APPLY (VALUES (o.x), (o.id)) AS v (v) ORDER BY o.id, v.v;"));
    }

    [Fact]
    public async Task A_values_column_takes_the_type_of_highest_precedence_among_its_rows()
    {
        // int and numeric(2,1) give numeric(2,1); varchar(3) and nvarchar(2) give nvarchar(3). So a column whose rows have
        // different types is converted, and only one whose rows have the same type keeps it.
        Assert.Equal(
            "t,p,s: numeric,2,1",
            await Select("SELECT TOP (1) CAST(SQL_VARIANT_PROPERTY(v.a, 'BaseType') AS sysname) AS t, CAST(SQL_VARIANT_PROPERTY(v.a, 'Precision') AS int) AS p, CAST(SQL_VARIANT_PROPERTY(v.a, 'Scale') AS int) AS s FROM (VALUES (1), (2.5)) AS v (a);"));
        Assert.Equal(
            "t,l: nvarchar,6",
            await Select("SELECT TOP (1) CAST(SQL_VARIANT_PROPERTY(v.a, 'BaseType') AS sysname) AS t, CAST(SQL_VARIANT_PROPERTY(v.a, 'MaxLength') AS int) AS l FROM (VALUES (CAST('a' AS varchar(3))), (CAST(N'b' AS nvarchar(2)))) AS v (a);"));
    }

    [Fact]
    public async Task A_values_column_of_string_literals_is_a_string_of_the_longest()
    {
        // varchar literals give varchar of the longest (at least 1: '' alone is varchar(1)); one N'' literal makes it nvarchar.
        const string Type = "CAST(SQL_VARIANT_PROPERTY(v.a, 'BaseType') AS sysname) AS t, CAST(SQL_VARIANT_PROPERTY(v.a, 'MaxLength') AS int) AS l";
        Assert.Equal("t,l,a: varchar,6,width;varchar,6,height;varchar,6,", await Select($"SELECT {Type}, v.a FROM (VALUES ('width'), ('height'), ('')) AS v (a);"));
        Assert.Equal("t,l: varchar,1", await Select($"SELECT {Type} FROM (VALUES ('')) AS v (a);"));
        Assert.Equal("t,l: nvarchar,6;nvarchar,6", await Select($"SELECT {Type} FROM (VALUES ('a'), (N'bcd')) AS v (a);"));
    }

    [Fact]
    public async Task Json_passes_through_a_values_list_as_json()
    {
        // json_query and a constructor in a VALUES row nest as JSON in FOR JSON, as through a derived table: not as text.
        Assert.Equal(
            """j: [{"j":{"a":1}}]""",
            await Select("SELECT (SELECT v.j FROM (VALUES (JSON_QUERY(N'{\"a\":1}'))) AS v (j) FOR JSON PATH) AS j;"));
        Assert.Equal(
            """j: [{"j":{"a":1}},{"j":[2]}]""",
            await Select("SELECT (SELECT v.j FROM (VALUES (JSON_QUERY(N'{\"a\":1}')), (JSON_QUERY(N'[2]'))) AS v (j) FOR JSON PATH) AS j;"));
        Assert.Equal(
            """j: [{"j":{"a":1}}]""",
            await Select("SELECT (SELECT x.j FROM dbo.obs_outer AS o CROSS APPLY (VALUES (JSON_OBJECT('a': 1))) AS x (j) WHERE o.id = 1 FOR JSON PATH) AS j;"));
    }

    // ---- views -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_view_binds_select_star_when_it_is_created()
    {
        // A column added to the table later is not in the view until sp_refreshview binds it again.
        string[] setup =
        [
            "CREATE TABLE dbo.obs_base (a int NOT NULL, b int NULL); INSERT INTO dbo.obs_base VALUES (1, NULL);",
            "CREATE VIEW dbo.obs_star AS SELECT * FROM dbo.obs_base;",
            "ALTER TABLE dbo.obs_base ADD c int NULL;",
        ];
        Assert.Equal("a,b: 1,null", (await Run(setup, "SELECT * FROM dbo.obs_star;")).ToString());
        Assert.Equal("a,b,c: 1,null,null", (await Run(setup, "EXEC sp_refreshview 'dbo.obs_star'; SELECT * FROM dbo.obs_star;")).ToString());
    }

    [Fact]
    public async Task A_view_column_list_names_every_column_and_no_more()
    {
        string[] table = ["CREATE TABLE dbo.obs_base (a int NOT NULL, b int NULL); INSERT INTO dbo.obs_base VALUES (1, NULL);"];
        Assert.Equal("x,y: 1,null", (await Run([.. table, "CREATE VIEW dbo.obs_named (x, y) AS SELECT a, b FROM dbo.obs_base;"], "SELECT * FROM dbo.obs_named;")).ToString());
        Assert.Equal(8158, (await Run(table, "EXEC (N'CREATE VIEW dbo.obs_short (x) AS SELECT a, b FROM dbo.obs_base;');")).Error);
    }

    [Fact]
    public async Task A_view_keeps_ansi_nulls_as_it_was_when_it_was_created()
    {
        // Created under ANSI_NULLS OFF, b = NULL is TRUE for a null b, whatever the caller's setting, as for a procedure.
        string[] setup =
        [
            "CREATE TABLE dbo.obs_base (a int NOT NULL, b int NULL); INSERT INTO dbo.obs_base VALUES (1, NULL), (2, 5);",
            "SET ANSI_NULLS OFF;",
            "CREATE VIEW dbo.obs_off AS SELECT a, b FROM dbo.obs_base WHERE b = NULL;",
            "SET ANSI_NULLS ON;",
        ];
        Assert.Equal("a,b: 1,null", (await Run(setup, "SELECT a, b FROM dbo.obs_off;")).ToString());
    }

    [Fact]
    public async Task Noexpand_is_refused_on_a_view_without_an_index_and_on_one_with_it_returns_the_same_rows()
    {
        string[] setup =
        [
            "CREATE TABLE dbo.obs_base (a int NOT NULL, b int NULL); INSERT INTO dbo.obs_base VALUES (1, NULL), (2, 5);",
            "CREATE VIEW dbo.obs_bound WITH SCHEMABINDING AS SELECT a, b FROM dbo.obs_base;",
            "CREATE VIEW dbo.obs_plain AS SELECT a, b FROM dbo.obs_base;",
            "CREATE UNIQUE CLUSTERED INDEX ix_obs_bound ON dbo.obs_bound (a);",
        ];
        Assert.Equal("a,b: 1,null;2,5", (await Run(setup, "SELECT a, b FROM dbo.obs_bound WITH (NOEXPAND) ORDER BY a;")).ToString());
        Assert.Equal("a,b: 1,null;2,5", (await Run(setup, "SELECT a, b FROM dbo.obs_bound ORDER BY a;")).ToString());
        Assert.Equal(8171, (await Run(setup, "SELECT a, b FROM dbo.obs_plain WITH (NOEXPAND);")).Error);
    }

    [Fact]
    public async Task For_system_time_other_than_as_of_repeats_a_key_through_a_table_or_a_view()
    {
        // A system-versioned table read FOR SYSTEM_TIME ALL, BETWEEN, FROM … TO or CONTAINED IN returns its history rows too,
        // one per version of a key; AS OF returns one version. Written on a view, the clause applies to the temporal tables
        // under it. So only AS OF keeps a key.
        // A write session is one transaction, in which an UPDATE leaves a history row of no length, which no clause returns;
        // so the history is written by hand, with a period of its own, before versioning is switched on.
        string[] setup =
        [
            "CREATE TABLE dbo.obs_hist_history (id int NOT NULL, name nvarchar(20) NOT NULL, vf datetime2 NOT NULL, vt datetime2 NOT NULL);",
            "INSERT INTO dbo.obs_hist_history VALUES (1, N'a', '2020-01-01', '2021-01-01');",
            """
            CREATE TABLE dbo.obs_hist (
                id int NOT NULL PRIMARY KEY, name nvarchar(20) NOT NULL,
                vf datetime2 GENERATED ALWAYS AS ROW START NOT NULL, vt datetime2 GENERATED ALWAYS AS ROW END NOT NULL,
                PERIOD FOR SYSTEM_TIME (vf, vt));
            """,
            "INSERT INTO dbo.obs_hist (id, name) VALUES (1, N'b');",
            "ALTER TABLE dbo.obs_hist SET (SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.obs_hist_history));",
            "CREATE VIEW dbo.obs_hist_all AS SELECT h.id, h.name FROM dbo.obs_hist FOR SYSTEM_TIME ALL AS h;",
            "CREATE VIEW dbo.obs_hist_now AS SELECT h.id, h.name FROM dbo.obs_hist AS h;",
        ];
        async Task<string> Count(string from) => (await Run(setup, $"SELECT COUNT(*) AS n FROM {from} WHERE h.id = 1;")).ToString();
        Assert.Equal("n: 2", await Count("dbo.obs_hist FOR SYSTEM_TIME ALL AS h"));
        Assert.Equal("n: 2", await Count("dbo.obs_hist FOR SYSTEM_TIME BETWEEN '0001-01-01' AND '9999-12-31' AS h"));
        Assert.Equal("n: 2", await Count("dbo.obs_hist FOR SYSTEM_TIME FROM '0001-01-01' TO '9999-12-31' AS h"));
        Assert.Equal("n: 2", await Count("dbo.obs_hist FOR SYSTEM_TIME CONTAINED IN ('2019-01-01', '9999-12-31 23:59:59.9999999') AS h"));
        Assert.Equal("n: 1", await Count("dbo.obs_hist FOR SYSTEM_TIME AS OF '2020-06-01' AS h"));
        Assert.Equal("n: 2", await Count("dbo.obs_hist_all AS h"));
        Assert.Equal("n: 2", await Count("dbo.obs_hist_now FOR SYSTEM_TIME ALL AS h"));
        Assert.Equal("n: 1", await Count("dbo.obs_hist_now AS h"));
    }

    [Fact]
    public async Task A_view_over_one_table_returns_each_of_its_rows_at_most_once()
    {
        // The rule keys rest on: a view that reads one table, without a join, APPLY or GROUP BY, returns some of the table's
        // rows, each once, so a key of the table that the view selects whole is a key of the view. Two rows of b = 5 stay two.
        string[] setup =
        [
            "CREATE TABLE dbo.obs_base (a int NOT NULL PRIMARY KEY, b int NULL); INSERT INTO dbo.obs_base VALUES (1, 5), (2, 5), (3, NULL);",
            "CREATE VIEW dbo.obs_some AS SELECT DISTINCT TOP (10) a, b FROM dbo.obs_base WHERE b IS NOT NULL;",
        ];
        Assert.Equal("a,b: 1,5;2,5", (await Run(setup, "SELECT a, b FROM dbo.obs_some ORDER BY a;")).ToString());
        Assert.Equal("a,b: 2,5", (await Run(setup, "SELECT a, b FROM dbo.obs_some WHERE a = 2;")).ToString());
    }
}
