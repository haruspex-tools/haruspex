using System.Data.Common;
using System.Globalization;
using Haruspex.DifferentialTests.Server;
using Microsoft.Data.SqlClient;

namespace Haruspex.DifferentialTests;

/// <summary>
/// What SQL Server folds to a constant where the answer changes a type: the integer digits an int counts beside a decimal, and
/// whether a value nests as JSON or as escaped text. Each expression is run at every compatibility level from 130 to 170, as it
/// is and with <c>OPTION (RECOMPILE)</c>, with parameters <c>@d</c> (21), <c>@r</c> (7) and <c>@v</c> (<c>N'[9]'</c>); an answer that is the same
/// everywhere is given alone, and one that is not names where each answer came from, so a rule that changes with the level or
/// the plan shows as such. Types are read as the server describes them (<c>sys.dm_exec_describe_first_result_set</c>) and as
/// the value carries them (<c>SQL_VARIANT_PROPERTY</c>), which must agree. The rules rest on these
/// (Haruspex.SqlServer.Inference's README.md, "What the server folds"; decision 0088). Observed on SQL Server 2025 RTM-CU10.
/// </summary>
public class FoldingObservationTests(SqlServerFixture server)
{
    private static readonly int[] Levels = [130, 140, 150, 160, 170];

    /// <summary>Each plan: as the query is, and with <c>OPTION (RECOMPILE)</c>, which folds a parameter's value.</summary>
    private static readonly (string Name, string Option)[] Plans = [("", ""), (" recompile", " OPTION (RECOMPILE)")];

    /// <summary>
    /// Invented tables: two products, the first with two tags and a reference, the second with none, no credits and no JSON;
    /// two months of working days; a one-row table whose CHECK pins its value; and views of a constant and of a table.
    /// </summary>
    private static readonly string[] Schema =
    [
        "CREATE SCHEMA k2;",
        """
        CREATE TABLE k2.product (id int NOT NULL PRIMARY KEY, name nvarchar(50) NOT NULL, budget decimal(19,4) NOT NULL,
                                 credits int NOT NULL CHECK (credits >= 0), ref int NULL, doc nvarchar(max) NULL);
        CREATE TABLE k2.tag (id int NOT NULL PRIMARY KEY, product_id int NOT NULL, name nvarchar(50) NOT NULL);
        CREATE TABLE k2.workday (month_start date NOT NULL PRIMARY KEY, days int NOT NULL);
        CREATE TABLE k2.one_row (id int NOT NULL PRIMARY KEY CHECK (id = 1), n int NOT NULL CHECK (n = 5));
        INSERT k2.product VALUES (1, N'kettle', 1234.5678, 7, 42, N'[1,2]'), (2, N'mug', 10, 0, NULL, NULL);
        INSERT k2.tag VALUES (1, 1, N'steel'), (2, 1, N'blue');
        INSERT k2.workday VALUES ('20260101', 21), ('20260201', 20);
        INSERT k2.one_row VALUES (1, 5);
        """,
        "CREATE VIEW k2.days21 AS SELECT 21 AS days;",
        "CREATE VIEW k2.constant_json AS SELECT JSON_QUERY(N'[1]') AS j;",
        "CREATE VIEW k2.tag_view AS SELECT t.product_id, t.name FROM k2.tag AS t;",
    ];

    /// <summary>
    /// Runs <paramref name="queries"/> everywhere: each is SQL text with <c>{0}</c> where the plan's option goes (replaced, not formatted, so JSON's braces stay), and returns one
    /// string. Gives each query's answer, alone when every level and plan agree, or as <c>answer @ 130, 140 recompile | …</c>.
    /// </summary>
    private async Task<string[]> Everywhere(Func<DbConnection, string, Task<string>> answer, IReadOnlyList<string> queries)
    {
        var token = TestContext.Current.CancellationToken;
        await using var scratch = await server.ScratchAsync();
        foreach (var batch in Schema)
            await scratch.ExecuteAsync(batch, token);

        var seen = queries.Select(_ => new List<(string Where, string Answer)>()).ToArray();
        foreach (var level in Levels)
        {
            await scratch.ExecuteAsync($"ALTER DATABASE CURRENT SET COMPATIBILITY_LEVEL = {level.ToString(CultureInfo.InvariantCulture)};", token);
            await using var connection = await scratch.OpenAsync(token);
            for (var i = 0; i < queries.Count; i++)
            {
                foreach (var (plan, option) in Plans)
                    seen[i].Add(($"{level}{plan}", await answer(connection, queries[i].Replace("{0}", option, StringComparison.Ordinal))));
            }
        }

        return [.. seen.Select(s => s.Select(x => x.Answer).Distinct(StringComparer.Ordinal).Count() == 1
            ? s[0].Answer
            : string.Join(" | ", s.GroupBy(x => x.Answer, StringComparer.Ordinal).Select(g => $"{g.Key} @ {string.Join(", ", g.Select(x => x.Where))}")))];
    }

    /// <summary>Runs a query through <c>sp_executesql</c> with the parameters, and gives its first value as text.</summary>
    private static async Task<string> Scalar(DbConnection connection, string query)
    {
        await using var command = Command(connection, "EXEC sp_executesql @q, N'@d int, @r int, @v nvarchar(max)', @d = 21, @r = 7, @v = N'[9]';", query);
        try
        {
            return await command.ExecuteScalarAsync(TestContext.Current.CancellationToken) is { } value and not DBNull
                ? Convert.ToString(value, CultureInfo.InvariantCulture)!
                : "NULL";
        }
        catch (SqlException e)
        {
            return $"error {e.Number.ToString(CultureInfo.InvariantCulture)}";
        }
    }

    /// <summary>
    /// The type of <c>SELECT {expression} AS v {from}</c>, as the server describes it and as the first row's value carries it,
    /// which must agree where the value is not NULL. The FROM gives the first product first.
    /// </summary>
    private async Task<string[]> Types(string from, params string[] expressions) =>
        await Everywhere(async (connection, query) =>
        {
            var parts = query.Split('\u0001');
            var described = await Described(connection, parts[0]);
            var carried = await Scalar(connection, parts[1]);
            // A NULL value carries no type (NULLIF of two equal literals), so only the description is read.
            return described == carried || carried == "decimal(,)" ? described : $"described {described}, carried {carried}";
        }, [.. expressions.Select(e => $"SELECT {e} AS v {from}{{0}}\u0001SELECT TOP (1) CONCAT('decimal(', CAST(SQL_VARIANT_PROPERTY(CAST({e} AS sql_variant), 'Precision') AS varchar(3)), ',', CAST(SQL_VARIANT_PROPERTY(CAST({e} AS sql_variant), 'Scale') AS varchar(3)), ')') {from} ORDER BY p.id{{0}}")]);

    /// <summary>
    /// How each of <paramref name="expressions"/> nests, over every product in turn: in FOR JSON, and as a value of <c>json_object</c>,
    /// as <c>for json | json_object</c>.
    /// </summary>
    private async Task<string[]> Nesting(string from, params string[] expressions) =>
        await Everywhere(Scalar, [.. expressions.Select(e =>
            $"SELECT CONCAT((SELECT p.id, {e} AS x {from} ORDER BY p.id FOR JSON PATH), ' | ', (SELECT JSON_OBJECT('x': {e}) AS o {from} ORDER BY p.id FOR JSON PATH)){{0}}")]);

    // ---- when the server derives a type --------------------------------------------------------------------------------

    /// <summary>A product beside a derived table, a CTE's and a view's literal, and the months' total days.</summary>
    private const string Budget = """
        FROM k2.product AS p
        CROSS JOIN (SELECT 21 AS days, 30 AS month_days) AS w
        CROSS JOIN (SELECT COUNT(*) AS calls FROM k2.tag) AS q
        CROSS JOIN (SELECT SUM(days) AS days FROM k2.workday) AS s
        CROSS JOIN k2.days21 AS v
        CROSS JOIN (VALUES (21)) AS r(days)
        """;

    [Fact]
    public async Task Only_expressions_over_literals_are_folded_when_a_type_is_derived()
    {
        // decimal(19,4) divided by an int of n digits is decimal(15 + max(6, 4 + n + 1), max(6, 4 + n + 1)): 10 digits give
        // decimal(30,15), 2 give decimal(22,7), 1 decimal(21,6). A literal, and NULLIF, IIF or COALESCE over literals only, are
        // folded and count their own; a column (a derived table's, a CTE's, a view's or a VALUES list's literal too), a
        // parameter, a subquery, ISNULL, and NULLIF, CASE, IIF or COALESCE reading one of those first, never are. The same at
        // every level, and with OPTION (RECOMPILE), even for a parameter.
        var types = await Types(Budget,
            "p.budget / 21", "p.budget / NULLIF(21, 0)", "p.budget / IIF(1 = 1, 5, 70)", "p.budget / COALESCE(NULL, 21)", "p.budget / COALESCE(5, p.ref)",
            "p.budget / w.days", "p.budget / (w.days)", "p.budget / v.days", "p.budget / r.days", "p.budget / NULLIF(w.days, 0)", "p.budget / NULLIF(v.days, 0)",
            "p.budget / NULLIF(p.credits + 1, 0)", "p.budget / NULLIF(@d, 0)", "p.budget / (SELECT 21)", "p.budget / ISNULL(5, 70)", "p.budget / ISNULL(w.days, 1)",
            "p.budget / COALESCE(p.ref, 5)", "p.budget / COALESCE(NULL, p.ref, 5)", "p.budget / COALESCE(w.days, 1)",
            "p.budget / CASE WHEN p.id > 0 THEN 5 ELSE 70 END", "p.budget / CASE WHEN @d > 0 THEN 5 ELSE 70 END", "p.budget / CASE p.id WHEN 1 THEN 5 ELSE 70 END",
            "p.budget / CASE WHEN q.calls > 0 THEN q.calls ELSE 1 END", "p.budget / IIF(w.days = 21, 5, 70)", "p.budget / CASE WHEN p.id > 0 THEN w.days ELSE 1 END",
            "p.budget / CASE WHEN p.id > 0 THEN 7 WHEN 1 = 1 THEN 5 ELSE 70 END", "p.budget / CASE WHEN 1 = 1 THEN 5 WHEN p.id > 0 THEN 7 ELSE 70 END",
            "CONVERT(decimal(19,6), p.credits + 1) / q.calls", "p.budget * w.month_days / NULLIF(s.days, 0)");
        Assert.Equal(
            [
                "decimal(22,7)", "decimal(22,7)", "decimal(21,6)", "decimal(22,7)", "decimal(21,6)",
                "decimal(30,15)", "decimal(30,15)", "decimal(30,15)", "decimal(30,15)", "decimal(30,15)", "decimal(30,15)",
                "decimal(30,15)", "decimal(30,15)", "decimal(30,15)", "decimal(30,15)", "decimal(30,15)",
                "decimal(30,15)", "decimal(30,15)", "decimal(30,15)",
                "decimal(30,15)", "decimal(30,15)", "decimal(30,15)",
                "decimal(30,15)", "decimal(30,15)", "decimal(30,15)",
                "decimal(30,15)", "decimal(21,6)",

                // decimal(19,6) by 10 digits; decimal(30,4) times by 10 digits is past 38, so its scale is cut to 12.
                "decimal(30,17)", "decimal(38,12)",
            ],
            types);
    }

    [Fact]
    public async Task A_condition_on_a_column_is_never_folded_but_one_with_a_constant_or_null_may_be()
    {
        // A comparison, IS NULL, IN, LIKE or BETWEEN over a column is never folded: not when it can't be true (a NOT NULL column
        // IS NULL, a CHECK broken, a contradiction), nor when it can't be false (a tautology, a column equal to itself, a
        // one-row table's CHECK), nor NOT, AND and OR of such conditions. With a constant, OR 1 = 1 and AND 1 = 0 are folded,
        // and so is a comparison with NULL, even of a column; NOT of a constant is too. EXISTS is never folded.
        var types = await Types(Budget + " CROSS JOIN k2.one_row AS o",
            "p.budget / IIF(p.id IS NULL, 5, 70)", "p.budget / IIF(p.credits < 0, 5, 70)", "p.budget / IIF(p.id > 0 AND p.id < 0, 5, 70)",
            "p.budget / IIF(p.id > 0 OR p.id <= 0, 5, 70)", "p.budget / IIF(p.id = p.id, 5, 70)", "p.budget / IIF(o.n = 5, 5, 70)",
            "p.budget / IIF(NOT (p.id > 0), 5, 70)", "p.budget / IIF(p.id > 0 AND p.ref > 0, 5, 70)", "p.budget / IIF(p.ref IS NULL AND p.ref IS NOT NULL, 5, 70)",
            "p.budget / IIF(p.id IN (1, 2), 5, 70)", "p.budget / IIF(p.name LIKE 'k%', 5, 70)", "p.budget / IIF(p.id BETWEEN 1 AND 2, 5, 70)",
            "p.budget / IIF(EXISTS (SELECT 1), 5, 70)", "p.budget / IIF((SELECT 1) = 1, 5, 70)",
            "p.budget / IIF(p.id > 0 OR 1 = 1, 5, 70)", "p.budget / IIF(p.id > 0 AND 1 = 0, 5, 70)", "p.budget / IIF(p.id = NULL, 5, 70)",
            "p.budget / IIF(NOT (1 = 1), 5, 70)", "p.budget / IIF(LEN('ab') = 2, 5, 70)");
        Assert.Equal(
            [
                "decimal(30,15)", "decimal(30,15)", "decimal(30,15)",
                "decimal(30,15)", "decimal(30,15)", "decimal(30,15)",
                "decimal(30,15)", "decimal(30,15)", "decimal(30,15)",
                "decimal(30,15)", "decimal(30,15)", "decimal(30,15)",
                "decimal(30,15)", "decimal(30,15)",
                "decimal(21,6)", "decimal(22,7)", "decimal(22,7)",
                "decimal(22,7)", "decimal(21,6)",
            ],
            types);
    }

    [Fact]
    public async Task A_derived_tables_literal_counts_its_own_digits_only_as_a_set_operations_query()
    {
        // Beside decimal(38,38) in a UNION, an int of n digits gives decimal(38, 38 - n). A derived table's, a view's or a nested
        // derived table's literal column counts its own digits as a query as it stands, in parentheses, or as what a constant
        // IIF folds to; inside any expression it counts 10, as it does in arithmetic (above).
        var types = await Everywhere(Described, [.. ((string[])[
            "w.one", "(w.one)", "IIF(1 = 1, w.one, 2)", "x.one", "v.days", "-w.one", "w.one + 0", "CAST(w.one AS int)", "COALESCE(w.one, 2)", "ISNULL(w.one, 2)",
            "NULLIF(w.one, 0)"]).Select(e =>
                $"SELECT CAST(0.5 AS decimal(38,38)) AS v FROM k2.product UNION ALL SELECT {e} FROM (SELECT 1 AS one) AS w CROSS JOIN (SELECT y.one FROM (SELECT 1 AS one) AS y) AS x CROSS JOIN k2.days21 AS v{{0}}")]);
        Assert.Equal(
            ["decimal(38,37)", "decimal(38,37)", "decimal(38,37)", "decimal(38,37)", "decimal(38,36)", "decimal(38,28)", "decimal(38,28)", "decimal(38,28)", "decimal(38,28)", "decimal(38,28)", "decimal(38,28)"],
            types);
    }

    [Fact]
    public async Task Nullif_of_two_literals_is_folded_inside_arithmetic_but_not_beside_other_values()
    {
        // NULLIF(1, 0) is tinyint, and as a UNION's query, or beside a COALESCE's or a CASE's other values, counts tinyint's 3
        // digits. In arithmetic it is folded, however deeply nested in NULLIFs of literals, to its first
        // literal, or to NULL (one digit) when the two are equal, the second a number or a string holding one; NULLIF of a literal and a column is not, and
        // counts tinyint's.
        var types = await Types(Budget,
            "CAST(0.5 AS decimal(38,38)) + NULLIF(1, 0)", "p.budget / NULLIF(5, 5)", "p.budget / NULLIF(70000, 0)", "p.budget / NULLIF(21, 0.0)", "p.budget / NULLIF(-21, 0)",
            "p.budget / NULLIF(21, p.id)", "CAST(0.5 AS decimal(38,38)) + NULLIF(70000, 0)", "CAST(0.5 AS decimal(38,38)) + NULLIF(5, 5.0)",
            "CAST(0.5 AS decimal(38,38)) + NULLIF(1, p.id)", "p.budget / NULLIF(21, '0')", "CAST(0.5 AS decimal(38,38)) + NULLIF(1, '1')",
            "COALESCE(CAST(0.5 AS decimal(38,38)), NULLIF(1, 0))", "IIF(p.id > 0, CAST(0.5 AS decimal(38,38)), NULLIF(1, 0))",
            "CAST(0.5 AS decimal(38,38)) + NULLIF(NULLIF(1, 0), 0)", "CAST(0.5 AS decimal(38,38)) + NULLIF(NULLIF(70000, 0), 0)",
            "CAST(0.5 AS decimal(38,38)) + NULLIF(1, NULLIF(0, 1))", "CAST(0.5 AS decimal(38,38)) + NULLIF(NULLIF(1, 1), 0)",
            "CAST(0.5 AS decimal(38,38)) + NULLIF(NULLIF(21, 0), 21)", "CAST(0.5 AS decimal(38,38)) + NULLIF(NULLIF(NULLIF(123, 0), 0), 0)");
        Assert.Equal(["decimal(38,37)", "decimal(21,6)", "decimal(25,10)", "decimal(22,7)", "decimal(22,7)", "decimal(23,8)", "decimal(38,33)", "decimal(38,37)", "decimal(38,35)", "decimal(22,7)", "decimal(38,37)", "decimal(38,35)", "decimal(38,35)",
            "decimal(38,37)", "decimal(38,33)", "decimal(38,37)", "decimal(38,37)", "decimal(38,37)", "decimal(38,35)"], types);
        Assert.Equal(
            ["decimal(38,35)"],
            await Everywhere(Described, ["SELECT CAST(0.5 AS decimal(38,38)) AS v FROM k2.product UNION ALL SELECT NULLIF(1, 0){0}"]));
    }

    [Fact]
    public async Task Division_by_nullif_of_zero_is_null_and_by_zero_an_error_unless_ansi_warnings_and_arithabort_are_both_off()
    {
        // The second product has no credits. NULLIF makes the division NULL under any options; dividing by the zero itself is
        // error 8134 unless the session has both ANSI_WARNINGS and ARITHABORT off, and then NULL.
        static string Divide(string options) => $"{options} SELECT CONCAT(ISNULL(CAST(p.budget / NULLIF(p.credits, 0) AS varchar(40)), 'NULL'), ' ', ISNULL(CAST(p.budget / p.credits AS varchar(40)), 'NULL')) FROM k2.product AS p WHERE p.id = 2{{0}};";
        Assert.Equal(
            ["error 8134", "error 8134", "error 8134", "NULL NULL"],
            await Everywhere(Scalar, [Divide("SET ANSI_WARNINGS ON; SET ARITHABORT ON;"), Divide("SET ANSI_WARNINGS OFF; SET ARITHABORT ON;"),
                Divide("SET ANSI_WARNINGS ON; SET ARITHABORT OFF;"), Divide("SET ANSI_WARNINGS OFF; SET ARITHABORT OFF;")]));
    }

    /// <summary>The server's description of the first column of <paramref name="query"/>'s first result set.</summary>
    private static async Task<string> Described(DbConnection connection, string query)
    {
        await using var describe = Command(connection, "SELECT system_type_name FROM sys.dm_exec_describe_first_result_set(@q, N'@d int, @r int, @v nvarchar(max)', 0);", query);
        return (string)(await describe.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    /// <summary>A command in the scratch database's session, given <paramref name="query"/> as its parameter <c>@q</c>.</summary>
    private static DbCommand Command(DbConnection connection, string text, string query)
    {
        var command = connection.CreateCommand();
#pragma warning disable CA2100 // Observation tests' own batches; the query observed goes in as a parameter.
        command.CommandText = text;
#pragma warning restore CA2100
        command.Parameters.Add(new SqlParameter("@q", System.Data.SqlDbType.NVarChar, -1) { Value = query });
        return command;
    }

    // ---- when the server decides whether a value nests as JSON ----------------------------------------------------------

    private const string Products = "FROM k2.product AS p CROSS JOIN (SELECT JSON_QUERY(N'[1]') AS j, N'[3]' AS t) AS w CROSS JOIN k2.constant_json AS v CROSS JOIN k2.one_row AS o";

    [Fact]
    public async Task Coalesce_over_json_built_from_rows_is_text_wherever_it_nests_at_every_level()
    {
        // COALESCE is folded only to its first argument. JSON built from rows never is: an aggregate's (even of a literal, even
        // without FROM, through a view of a table), a FOR JSON subquery's, cast or converted to nvarchar(max), json_query of one
        // or of a table's column, and json_object of a column (never NULL, so its fallback is never taken); so COALESCE over it is text in FOR JSON and in json_object alike,
        // the fallback too. json_query of a constant is folded, through a subquery, a derived table or a view, and nests as JSON. json_query of a
        // parameter is text here, even under OPTION (RECOMPILE), but a CASE on one is not (below), so neither is relied on. As for
        // a CASE, in json_object a COALESCE whose last argument is JSON built here (json_array, a FOR JSON subquery, json_object)
        // nests as JSON, though it is text in FOR JSON.
        var nesting = await Nesting(Products,
            "COALESCE((SELECT CONVERT(nvarchar(max), JSON_ARRAYAGG(t.name ORDER BY t.name)) FROM k2.tag AS t WHERE t.product_id = p.id), N'[]')",
            "COALESCE((SELECT CAST(JSON_ARRAYAGG(t.name ORDER BY t.name) AS nvarchar(max)) FROM k2.tag AS t WHERE t.product_id = p.id), N'[]')",
            "COALESCE((SELECT JSON_ARRAYAGG(t.name ORDER BY t.name) FROM k2.tag_view AS t WHERE t.product_id = p.id), JSON_QUERY(N'[]'))",
            "COALESCE((SELECT JSON_OBJECTAGG(t.name: t.id) FROM k2.tag AS t WHERE t.product_id = p.id), N'{}')",
            "COALESCE((SELECT JSON_ARRAYAGG(1)), N'[]')",
            "COALESCE(JSON_QUERY((SELECT t.name FROM k2.tag AS t WHERE t.product_id = p.id ORDER BY t.id FOR JSON PATH)), N'[]')",
            "COALESCE(JSON_QUERY(p.doc), N'[]')",
            "COALESCE(JSON_OBJECT('a': p.ref), N'{}')",
            "COALESCE((SELECT TOP (1) JSON_QUERY(N'[1]') FROM k2.tag), N'[]')",
            "COALESCE(w.j, N'[]')",
            "COALESCE(v.j, N'[]')",
            "COALESCE(JSON_QUERY(@v), N'[]')",
            "COALESCE((SELECT JSON_ARRAYAGG(t.name ORDER BY t.id) FROM k2.tag AS t WHERE t.product_id = p.id), JSON_ARRAY())",
            "COALESCE((SELECT t.name FROM k2.tag AS t WHERE t.product_id = p.id ORDER BY t.id FOR JSON PATH), (SELECT 1 AS n FOR JSON PATH))",
            "COALESCE((SELECT JSON_ARRAYAGG(t.name ORDER BY t.id) FROM k2.tag AS t WHERE t.product_id = p.id), JSON_OBJECT('k': p.id))");
        Assert.Equal(
            [
                """[{"id":1,"x":"[\"blue\",\"steel\"]"},{"id":2,"x":"[]"}] | [{"o":{"x":"[\"blue\",\"steel\"]"}},{"o":{"x":"[]"}}]""",
                """[{"id":1,"x":"[\"blue\",\"steel\"]"},{"id":2,"x":"[]"}] | [{"o":{"x":"[\"blue\",\"steel\"]"}},{"o":{"x":"[]"}}]""",
                """[{"id":1,"x":"[\"blue\",\"steel\"]"},{"id":2,"x":"[]"}] | [{"o":{"x":"[\"blue\",\"steel\"]"}},{"o":{"x":"[]"}}]""",
                """[{"id":1,"x":"{\"steel\":1,\"blue\":2}"},{"id":2,"x":"{}"}] | [{"o":{"x":"{\"steel\":1,\"blue\":2}"}},{"o":{"x":"{}"}}]""",
                """[{"id":1,"x":"[1]"},{"id":2,"x":"[1]"}] | [{"o":{"x":"[1]"}},{"o":{"x":"[1]"}}]""",
                """[{"id":1,"x":"[{\"name\":\"steel\"},{\"name\":\"blue\"}]"},{"id":2,"x":"[]"}] | [{"o":{"x":"[{\"name\":\"steel\"},{\"name\":\"blue\"}]"}},{"o":{"x":"[]"}}]""",
                """[{"id":1,"x":"[1,2]"},{"id":2,"x":"[]"}] | [{"o":{"x":"[1,2]"}},{"o":{"x":"[]"}}]""",
                """[{"id":1,"x":"{\"a\":42}"},{"id":2,"x":"{\"a\":null}"}] | [{"o":{"x":"{\"a\":42}"}},{"o":{"x":"{\"a\":null}"}}]""",
                """[{"id":1,"x":[1]},{"id":2,"x":[1]}] | [{"o":{"x":[1]}},{"o":{"x":[1]}}]""",
                """[{"id":1,"x":[1]},{"id":2,"x":[1]}] | [{"o":{"x":[1]}},{"o":{"x":[1]}}]""",
                """[{"id":1,"x":[1]},{"id":2,"x":[1]}] | [{"o":{"x":[1]}},{"o":{"x":[1]}}]""",
                """[{"id":1,"x":"[9]"},{"id":2,"x":"[9]"}] | [{"o":{"x":"[9]"}},{"o":{"x":"[9]"}}]""",
                """[{"id":1,"x":"[\"steel\",\"blue\"]"},{"id":2,"x":"[]"}] | [{"o":{"x":["steel","blue"]}},{"o":{"x":[]}}]""",
                """[{"id":1,"x":"[{\"name\":\"steel\"},{\"name\":\"blue\"}]"},{"id":2,"x":"[{\"n\":1}]"}] | [{"o":{"x":[{"name":"steel"},{"name":"blue"}]}},{"o":{"x":[{"n":1}]}}]""",
                """[{"id":1,"x":"[\"steel\",\"blue\"]"},{"id":2,"x":"{\"k\":2}"}] | [{"o":{"x":["steel","blue"]}},{"o":{"x":{"k":2}}}]""",
            ],
            nesting);
    }

    [Fact]
    public async Task A_case_over_json_on_a_tables_column_is_text_where_its_last_result_is_null_or_a_literal()
    {
        // A CASE or IIF whose condition reads a table's column is never folded to one result, and in FOR JSON it is text. In
        // json_object it is text too where its last result is absent, NULL, a string literal or json_query of one, but JSON
        // where its last result is JSON built here. A condition on a parameter is folded under OPTION (RECOMPILE), and the CASE
        // is then JSON in json_object.
        var nesting = await Nesting(Products,
            "CASE WHEN p.ref IS NOT NULL THEN JSON_QUERY(JSON_OBJECT('system': 'x', 'id': p.ref)) END",
            "CASE WHEN p.ref IS NOT NULL THEN JSON_OBJECT('id': p.ref) END",
            "IIF(p.ref IS NOT NULL, JSON_OBJECT('id': p.ref), NULL)",
            "CASE WHEN p.ref IS NOT NULL THEN JSON_QUERY(JSON_OBJECT('id': p.ref)) ELSE N'[]' END",
            "CASE WHEN p.ref = 42 THEN JSON_OBJECT('id': p.ref) ELSE JSON_QUERY(N'[]') END",
            "CASE WHEN o.n = 5 THEN JSON_OBJECT('id': p.id) END",
            "CASE WHEN p.ref IS NULL THEN NULL ELSE JSON_OBJECT('id': p.ref) END",
            "CASE WHEN p.ref IS NOT NULL THEN JSON_OBJECT('id': p.ref) ELSE JSON_ARRAY(1, 2) END",
            "CASE WHEN @r IS NOT NULL THEN JSON_QUERY(JSON_OBJECT('id': @r)) END",
            "CASE WHEN p.ref IS NOT NULL OR 1 = 1 THEN JSON_OBJECT('id': p.id) END",
            "CASE WHEN p.ref = 42 THEN (SELECT t.name FROM k2.tag AS t WHERE t.product_id = p.id ORDER BY t.id FOR JSON PATH) ELSE N'[]' END");
        Assert.Equal(
            [
                """[{"id":1,"x":"{\"system\":\"x\",\"id\":42}"},{"id":2}] | [{"o":{"x":"{\"system\":\"x\",\"id\":42}"}},{"o":{"x":null}}]""",
                """[{"id":1,"x":"{\"id\":42}"},{"id":2}] | [{"o":{"x":"{\"id\":42}"}},{"o":{"x":null}}]""",
                """[{"id":1,"x":"{\"id\":42}"},{"id":2}] | [{"o":{"x":"{\"id\":42}"}},{"o":{"x":null}}]""",
                """[{"id":1,"x":"{\"id\":42}"},{"id":2,"x":"[]"}] | [{"o":{"x":"{\"id\":42}"}},{"o":{"x":"[]"}}]""",
                """[{"id":1,"x":"{\"id\":42}"},{"id":2,"x":"[]"}] | [{"o":{"x":"{\"id\":42}"}},{"o":{"x":"[]"}}]""",
                """[{"id":1,"x":"{\"id\":1}"},{"id":2,"x":"{\"id\":2}"}] | [{"o":{"x":"{\"id\":1}"}},{"o":{"x":"{\"id\":2}"}}]""",
                """[{"id":1,"x":"{\"id\":42}"},{"id":2}] | [{"o":{"x":{"id":42}}},{"o":{"x":null}}]""",
                """[{"id":1,"x":"{\"id\":42}"},{"id":2,"x":"[1,2]"}] | [{"o":{"x":{"id":42}}},{"o":{"x":[1,2]}}]""",
                """[{"id":1,"x":"{\"id\":7}"},{"id":2,"x":"{\"id\":7}"}] | [{"o":{"x":"{\"id\":7}"}},{"o":{"x":"{\"id\":7}"}}] @ 130, 140, 150, 160, 170"""
                    + """ | [{"id":1,"x":"{\"id\":7}"},{"id":2,"x":"{\"id\":7}"}] | [{"o":{"x":{"id":7}}},{"o":{"x":{"id":7}}}] @ 130 recompile, 140 recompile, 150 recompile, 160 recompile, 170 recompile""",
                """[{"id":1,"x":{"id":1}},{"id":2,"x":{"id":2}}] | [{"o":{"x":{"id":1}}},{"o":{"x":{"id":2}}}]""",
                """[{"id":1,"x":"[{\"name\":\"steel\"},{\"name\":\"blue\"}]"},{"id":2,"x":"[]"}] | [{"o":{"x":"[{\"name\":\"steel\"},{\"name\":\"blue\"}]"}},{"o":{"x":"[]"}}]""",
            ],
            nesting);
    }

    [Fact]
    public async Task Json_of_a_type_shorter_than_max_stays_json_through_case_and_coalesce()
    {
        // JSON that is not nvarchar(max) (json_query of a literal is nvarchar(4000), of a cast to nvarchar(200) that type) nests
        // as JSON through a CASE on a table's column, a COALESCE and a NULLIF, where the same over nvarchar(max) JSON is text.
        var nesting = await Nesting(Products,
            "CASE WHEN p.ref IS NOT NULL THEN JSON_QUERY(N'{\"a\":1}') END",
            "CASE WHEN p.ref IS NOT NULL THEN JSON_QUERY(CAST(JSON_OBJECT('id': p.ref) AS nvarchar(200))) END",
            "COALESCE(JSON_QUERY(CAST(JSON_OBJECT('id': p.ref) AS nvarchar(200))), N'[]')",
            "NULLIF(JSON_QUERY(CAST(JSON_OBJECT('id': p.ref) AS nvarchar(200))), N'')");
        Assert.Equal(
            [
                """[{"id":1,"x":{"a":1}},{"id":2}] | [{"o":{"x":{"a":1}}},{"o":{"x":null}}]""",
                """[{"id":1,"x":{"id":42}},{"id":2}] | [{"o":{"x":{"id":42}}},{"o":{"x":null}}]""",
                """[{"id":1,"x":{"id":42}},{"id":2,"x":{"id":null}}] | [{"o":{"x":{"id":42}}},{"o":{"x":{"id":null}}}]""",
                """[{"id":1,"x":{"id":42}},{"id":2,"x":{"id":null}}] | [{"o":{"x":{"id":42}}},{"o":{"x":{"id":null}}}]""",
            ],
            nesting);
    }

    [Fact]
    public async Task Nullif_over_json_text_is_text_or_null_in_every_context()
    {
        // NULLIF over JSON text (FOR JSON WITHOUT_ARRAY_WRAPPER) is that text, or NULL when it equals the other argument, and
        // text wherever it nests. Over live JSON it is text in FOR JSON but JSON in json_object.
        var nesting = await Nesting(Products,
            "NULLIF((SELECT TOP (1) t.name AS n FROM k2.tag AS t WHERE t.product_id = p.id ORDER BY t.id FOR JSON PATH, WITHOUT_ARRAY_WRAPPER), N'')",
            """NULLIF((SELECT TOP (1) t.name AS n FROM k2.tag AS t WHERE t.product_id = p.id ORDER BY t.id FOR JSON PATH, WITHOUT_ARRAY_WRAPPER), N'{"n":"steel"}')""",
            "NULLIF((SELECT t.name FROM k2.tag AS t WHERE t.product_id = p.id ORDER BY t.id FOR JSON PATH), N'')",
            "NULLIF(JSON_QUERY(p.doc), N'[]')");
        Assert.Equal(
            [
                """[{"id":1,"x":"{\"n\":\"steel\"}"},{"id":2}] | [{"o":{"x":"{\"n\":\"steel\"}"}},{"o":{"x":null}}]""",
                """[{"id":1},{"id":2}] | [{"o":{"x":null}},{"o":{"x":null}}]""",
                """[{"id":1,"x":"[{\"name\":\"steel\"},{\"name\":\"blue\"}]"},{"id":2}] | [{"o":{"x":[{"name":"steel"},{"name":"blue"}]}},{"o":{"x":null}}]""",
                """[{"id":1,"x":"[1,2]"},{"id":2}] | [{"o":{"x":[1,2]}},{"o":{"x":null}}]""",
            ],
            nesting);
    }

    [Fact]
    public async Task A_union_over_json_built_from_rows_is_text_in_every_row()
    {
        // A set operation's column has one form for every row. A UNION's is text, whatever the order, when one query's JSON is
        // built from rows (json_query of a column, a FOR JSON or aggregate subquery, json_object) or is json_object of
        // constants, and JSON only when every query's JSON is json_query of a constant, a text query then written into the JSON
        // as it is. EXCEPT keeps the first query's form, JSON for json_query of a column.
        static string Union(string sql) => $"SELECT CONCAT((SELECT u.k, u.x FROM ({sql}) AS u ORDER BY u.k FOR JSON PATH), ' | ', (SELECT JSON_OBJECT('x': u.x) AS o FROM ({sql}) AS u ORDER BY u.k FOR JSON PATH)){{0}}";
        var nesting = await Everywhere(Scalar, [
            Union("SELECT p.id AS k, JSON_QUERY(JSON_OBJECT('a': p.id)) AS x FROM k2.product AS p UNION ALL SELECT 9, JSON_QUERY(JSON_OBJECT('b': 2))"),
            Union("SELECT 0 AS k, JSON_QUERY(N'[]') AS x UNION ALL SELECT p.id, JSON_QUERY(p.doc) FROM k2.product AS p WHERE p.doc IS NOT NULL"),
            Union("SELECT 9 AS k, (SELECT 1 AS a FOR JSON PATH) AS x UNION ALL SELECT 0, JSON_QUERY(N'[]')"),
            Union("SELECT p.id AS k, JSON_QUERY((SELECT t.name FROM k2.tag AS t WHERE t.product_id = p.id ORDER BY t.id FOR JSON PATH)) AS x FROM k2.product AS p UNION ALL SELECT 9, JSON_QUERY((SELECT 1 AS a FOR JSON PATH))"),
            Union("SELECT 9 AS k, (SELECT JSON_ARRAYAGG(t.name ORDER BY t.id) FROM k2.tag AS t) AS x UNION ALL SELECT 0, JSON_QUERY(N'[]')"),
            Union("SELECT 9 AS k, JSON_OBJECT('b': 2) AS x UNION ALL SELECT 0, JSON_QUERY(N'[]')"),
            Union("SELECT 1 AS k, JSON_QUERY(N'[1]') AS x UNION ALL SELECT 9, N'plain'"),
            Union("SELECT p.id AS k, JSON_QUERY(p.doc) AS x FROM k2.product AS p WHERE p.doc IS NOT NULL EXCEPT SELECT 0, JSON_QUERY(N'[]')"),
        ]);
        Assert.Equal(
            [
                """[{"k":1,"x":"{\"a\":1}"},{"k":2,"x":"{\"a\":2}"},{"k":9,"x":"{\"b\":2}"}] | [{"o":{"x":"{\"a\":1}"}},{"o":{"x":"{\"a\":2}"}},{"o":{"x":"{\"b\":2}"}}]""",
                """[{"k":0,"x":"[]"},{"k":1,"x":"[1,2]"}] | [{"o":{"x":"[]"}},{"o":{"x":"[1,2]"}}]""",
                """[{"k":0,"x":"[]"},{"k":9,"x":"[{\"a\":1}]"}] | [{"o":{"x":"[]"}},{"o":{"x":"[{\"a\":1}]"}}]""",
                """[{"k":1,"x":"[{\"name\":\"steel\"},{\"name\":\"blue\"}]"},{"k":2},{"k":9,"x":"[{\"a\":1}]"}] | [{"o":{"x":"[{\"name\":\"steel\"},{\"name\":\"blue\"}]"}},{"o":{"x":null}},{"o":{"x":"[{\"a\":1}]"}}]""",
                """[{"k":0,"x":"[]"},{"k":9,"x":"[\"steel\",\"blue\"]"}] | [{"o":{"x":"[]"}},{"o":{"x":"[\"steel\",\"blue\"]"}}]""",
                """[{"k":0,"x":"[]"},{"k":9,"x":"{\"b\":2}"}] | [{"o":{"x":"[]"}},{"o":{"x":"{\"b\":2}"}}]""",
                """[{"k":1,"x":[1]},{"k":9,"x":plain}] | [{"o":{"x":[1]}},{"o":{"x":plain}}]""",
                """[{"k":1,"x":[1,2]}] | [{"o":{"x":[1,2]}}]""",
            ],
            nesting);
    }
}
