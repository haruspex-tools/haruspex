using System.Globalization;
using Haruspex.DifferentialTests.Server;
using Microsoft.Data.SqlClient;

namespace Haruspex.DifferentialTests;

/// <summary>
/// What a caller without <c>UNMASK</c> reads of a masked column, and of what is computed from one, observed as a user who holds
/// only <c>SELECT</c> and <c>EXECUTE</c>: the rules masked columns are inferred by (the inference README's "Masked columns";
/// docs/decisions/0118-a-masked-column-claims-only-its-type-nullability-and-greatest-length.md). Each value is read as SqlClient
/// hands it over: a string in brackets, so that padding shows, and bytes in hex. Observed on SQL Server 2025 RTM-CU10.
/// </summary>
public class MaskingObservationTests(SqlServerFixture server)
{
    /// <summary>A user without <c>UNMASK</c>, made for each test and rolled back with it.</summary>
    private const string Reader = "CREATE USER obs_mask_reader WITHOUT LOGIN; GRANT SELECT TO obs_mask_reader; GRANT EXECUTE TO obs_mask_reader;";

    /// <summary>
    /// Runs <paramref name="setup"/> as the tests' own login, then <paramref name="query"/> as <see cref="Reader"/>: each result set
    /// as <c>name: value,value;value,value</c>, sets apart by <c> | </c>, or the error's number after <c>!</c>.
    /// </summary>
    private async Task<string> AsReader(string[] setup, string query, string user = "obs_mask_reader")
    {
        server.Require();
        var cancel = TestContext.Current.CancellationToken;
        await using var session = await server.WriteAsync(cancel);
        foreach (var batch in (string[])[Reader, .. setup])
        {
            await using var create = session.Command(batch);
            await create.ExecuteNonQueryAsync(cancel);
        }

        await using var command = session.Command($"EXECUTE AS USER = N'{user}'; {query}; REVERT;");
        var sets = new List<string>();
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancel);
            do
            {
                if (reader.FieldCount == 0)
                    continue;
                var rows = new List<string>();
                while (await reader.ReadAsync(cancel))
                    rows.Add(string.Join(",", Enumerable.Range(0, reader.FieldCount).Select(i => Text(reader.GetValue(i)))));
                sets.Add($"{string.Join(",", Enumerable.Range(0, reader.FieldCount).Select(reader.GetName))}: {string.Join(";", rows)}");
            }
            while (await reader.NextResultAsync(cancel));
        }
        catch (SqlException e)
        {
            sets.Add($"! {e.Number}");
        }

        return string.Join(" | ", sets);
    }

    private static string Text(object value) => value switch
    {
        DBNull => "null",
        string s => $"[{s}]",
        byte[] bytes => "0x" + Convert.ToHexString(bytes),
        bool b => b ? "true" : "false",
        DateTime d => d.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture).TrimEnd('.'),
        DateTimeOffset d => d.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF zzz", CultureInfo.InvariantCulture),
        TimeSpan t => t.ToString("c", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
    };

    /// <summary>
    /// A masked column of each type each function takes, holding a value and NULL: what the reader receives of each. Every mask is
    /// of the column's type and never longer than it, but not padded to a fixed length, and as short as the mask however long the
    /// value: so an empty string becomes four characters, or a partial mask's padding, and a <c>char(6)</c> four. NULL stays NULL,
    /// and nothing else becomes it.
    /// </summary>
    public static TheoryData<string, string, string> Masks { get; } = new()
    {
        { "int MASKED WITH (FUNCTION = 'default()')", "4", "0;null" },
        { "tinyint MASKED WITH (FUNCTION = 'default()')", "200", "0;null" },
        { "decimal(5,2) MASKED WITH (FUNCTION = 'default()')", "123.45", "0.00;null" },
        { "money MASKED WITH (FUNCTION = 'default()')", "12.34", "0.0000;null" },
        { "float MASKED WITH (FUNCTION = 'default()')", "1.5", "0;null" },
        { "bit MASKED WITH (FUNCTION = 'default()')", "1", "false;null" },
        { "char(2) MASKED WITH (FUNCTION = 'default()')", "'AB'", "[xx];null" },
        { "char(6) MASKED WITH (FUNCTION = 'default()')", "'ABCDEF'", "[xxxx];null" },
        { "nchar(6) MASKED WITH (FUNCTION = 'default()')", "N'ABCDEF'", "[xxxx];null" },
        { "varchar(1) MASKED WITH (FUNCTION = 'default()')", "'Z'", "[x];null" },
        { "varchar(10) MASKED WITH (FUNCTION = 'default()')", "''", "[xxxx];null" },
        { "nvarchar(max) MASKED WITH (FUNCTION = 'default()')", "N'{\"a\":1}'", "[xxxx];null" },
        { "binary(4) MASKED WITH (FUNCTION = 'default()')", "0x01020304", "0x30;null" },
        { "varbinary(10) MASKED WITH (FUNCTION = 'default()')", "0x0102", "0x30;null" },
        { "date MASKED WITH (FUNCTION = 'default()')", "'2024-05-06'", "1900-01-01 00:00:00;null" },
        { "time MASKED WITH (FUNCTION = 'default()')", "'12:34:56'", "00:00:00;null" },
        { "datetime2(3) MASKED WITH (FUNCTION = 'default()')", "'2024-05-06T07:08:09.123'", "1900-01-01 00:00:00;null" },
        { "uniqueidentifier MASKED WITH (FUNCTION = 'default()')", "'6F9619FF-8B86-D011-B42D-00C04FC964FF'", "00000000-0000-0000-0000-000000000000;null" },
        { "xml MASKED WITH (FUNCTION = 'default()')", "'<a/>'", "[<masked />];null" },
        { "json MASKED WITH (FUNCTION = 'default()')", "'{\"a\":1}'", "[{\"masked\":true}];null" },
        { "varchar(5) MASKED WITH (FUNCTION = 'email()')", "'a@b.c'", "[aXXX@];null" },
        { "nvarchar(40) MASKED WITH (FUNCTION = 'email()')", "N'someone@example.com'", "[sXXX@XXXX.com];null" },
        { "varchar(10) MASKED WITH (FUNCTION = 'email()')", "''", "[xxxx];null" },
        { "varchar(4) MASKED WITH (FUNCTION = 'partial(1, \"XXXXXXXX\", 1)')", "'abcd'", "[aXXX];null" },
        { "varchar(20) MASKED WITH (FUNCTION = 'partial(2, \"-\", 0)')", "'abcdefghijklmnopqrst'", "[ab-];null" },
        { "varchar(4) MASKED WITH (FUNCTION = 'partial(1, \"XX\", 1)')", "''", "[XX];null" },
        { "datetime2(3) MASKED WITH (FUNCTION = 'datetime(\"Y\")')", "'2024-05-06T07:08:09.123'", "2000-05-06 07:08:09;null" },
        { "datetimeoffset MASKED WITH (FUNCTION = 'datetime(\"M\")')", "'2024-05-06T07:08:09+02:00'", "2024-01-06 07:08:09 +00:00;null" },
    };

    [Theory]
    [MemberData(nameof(Masks))]
    public async Task Each_mask_is_a_value_of_its_column_s_type_no_longer_than_it_and_null_only_for_null(string declaration, string stored, string expected)
    {
        var observed = await AsReader(
            [$"CREATE TABLE dbo.obs_mask (id int NOT NULL PRIMARY KEY, v {declaration} NULL); INSERT dbo.obs_mask VALUES (1, {stored}), (2, NULL);"],
            "SELECT v FROM dbo.obs_mask ORDER BY id");

        Assert.Equal($"v: {expected}", observed);
    }

    [Fact]
    public async Task A_random_mask_draws_from_its_range_which_must_lie_in_the_column_s_type()
    {
        var drawn = await AsReader(
            ["""
            CREATE TABLE dbo.obs_mask (id int NOT NULL PRIMARY KEY, n int MASKED WITH (FUNCTION = 'random(10, 20)') NOT NULL CHECK (n BETWEEN 1 AND 5),
                d decimal(4,1) MASKED WITH (FUNCTION = 'random(1, 5)') NOT NULL);
            INSERT dbo.obs_mask SELECT TOP (200) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), 3, 2.5 FROM sys.all_columns;
            """],
            "SELECT CASE WHEN COUNT(*) = 200 THEN 'all' END AS rows FROM dbo.obs_mask; SELECT n, d FROM dbo.obs_mask");
        var values = drawn.Split(" | ")[1]["n,d: ".Length..].Split(';').Select(r => r.Split(',')).ToList();

        Assert.All(values, v => Assert.InRange(int.Parse(v[0], CultureInfo.InvariantCulture), 10, 20));
        Assert.All(values, v => Assert.InRange(decimal.Parse(v[1], CultureInfo.InvariantCulture), 1m, 5m));
        Assert.Contains(values, v => v[0] != "15");

        // A range the type can't hold is refused when the column is declared.
        Assert.Equal(16005, (await ObservedCall.Run(server, [], "CREATE TABLE dbo.obs_mask_wide (a tinyint MASKED WITH (FUNCTION = 'random(1, 1000)') NULL);")).Error);
        Assert.Equal(16005, (await ObservedCall.Run(server, [], "CREATE TABLE dbo.obs_mask_wide (a decimal(4,1) MASKED WITH (FUNCTION = 'random(1, 100000)') NULL);")).Error);
    }

    [Fact]
    public async Task An_expression_computed_from_a_masked_column_is_masked_as_a_whole()
    {
        var observed = await AsReader(
            ["""
            CREATE TABLE dbo.obs_mask (id int NOT NULL PRIMARY KEY, n int MASKED WITH (FUNCTION = 'default()') NULL, c char(6) MASKED WITH (FUNCTION = 'default()') NOT NULL, v varchar(1) MASKED WITH (FUNCTION = 'default()') NOT NULL);
            INSERT dbo.obs_mask VALUES (1, 4, 'ABCDEF', 'Z'), (2, NULL, 'ABCDEF', 'Y');
            """],
            """
            SELECT n + 1 AS plus, '[' + v + ']' AS wrapped, DATALENGTH(c) AS length, CAST(c AS char(10)) AS cast, ISNULL(n, 7) AS isnull_,
                   NULLIF(n, 4) AS nullif_, CASE WHEN id = 1 THEN n ELSE 9 END AS chosen, COUNT(n) OVER () AS counted
            FROM dbo.obs_mask ORDER BY id
            """);

        // Every one is its type's mask, '[' + v + ']' a varchar(3), even 9 and 7, which don't come from the column; NULL is NULL.
        Assert.Equal("plus,wrapped,length,cast,isnull_,nullif_,chosen,counted: 0,[xxx],0,[xxxx],0,null,0,0;null,[xxx],0,[xxxx],0,null,0,0", observed);
    }

    [Fact]
    public async Task A_condition_on_a_masked_column_leaves_the_value_it_chooses_unmasked()
    {
        var observed = await AsReader(
            ["CREATE TABLE dbo.obs_mask (id int NOT NULL PRIMARY KEY, n int MASKED WITH (FUNCTION = 'default()') NOT NULL); INSERT dbo.obs_mask VALUES (1, 4), (2, 2);"],
            "SELECT id, CASE WHEN n = 4 THEN 'a' ELSE 'b' END AS chosen, IIF(n = 2, 'y', 'n') AS picked FROM dbo.obs_mask WHERE n > 1 ORDER BY id");

        Assert.Equal("id,chosen,picked: 1,[a],[n];2,[b],[y]", observed);
    }

    [Fact]
    public async Task A_set_operation_s_column_is_masked_when_one_query_s_is()
    {
        var observed = await AsReader(
            ["CREATE TABLE dbo.obs_mask (id int NOT NULL PRIMARY KEY, n int MASKED WITH (FUNCTION = 'default()') NOT NULL); INSERT dbo.obs_mask VALUES (1, 4);"],
            "SELECT n AS v FROM dbo.obs_mask UNION ALL SELECT 42");

        Assert.Equal("v: 0;0", observed);
    }

    [Fact]
    public async Task Json_computed_from_a_masked_column_is_its_mask_and_not_json()
    {
        var observed = await AsReader(
            ["CREATE TABLE dbo.obs_mask (id int NOT NULL PRIMARY KEY, n int MASKED WITH (FUNCTION = 'default()') NOT NULL); INSERT dbo.obs_mask VALUES (1, 4);"],
            """
            SELECT JSON_OBJECT('n': n) AS made, (SELECT n FROM dbo.obs_mask FOR JSON PATH) AS nested FROM dbo.obs_mask;
            SELECT id, n FROM dbo.obs_mask FOR JSON PATH;
            SELECT id, (SELECT i.n FROM dbo.obs_mask AS i WHERE i.id = o.id FOR JSON PATH) AS kids FROM dbo.obs_mask AS o FOR JSON PATH
            """);

        // A column's own value is masked inside the JSON; JSON computed from one is written as its mask, without quotes.
        Assert.Equal(
            "made,nested: [xxxx],[xxxx] | JSON_F52E2B61-18A1-11d1-B105-00805F49916B: [[{\"id\":1,\"n\":0}]] | JSON_F52E2B61-18A1-11d1-B105-00805F49916B: [[{\"id\":1,\"kids\":xxxx}]]",
            observed);
    }

    [Fact]
    public async Task A_rowset_function_given_a_masked_argument_returns_every_column_as_its_mask()
    {
        var observed = await AsReader(
            [
                "CREATE TABLE dbo.obs_mask (id int NOT NULL PRIMARY KEY, n int MASKED WITH (FUNCTION = 'default()') NOT NULL, s varchar(10) MASKED WITH (FUNCTION = 'default()') NOT NULL, j nvarchar(100) MASKED WITH (FUNCTION = 'default()') NOT NULL); INSERT dbo.obs_mask VALUES (1, 2, 'a,b', N'{\"k\":1}');",
                "CREATE FUNCTION dbo.obs_mask_rows (@x int) RETURNS @r TABLE (v char(6), c int CHECK (c BETWEEN 5 AND 9), b binary(4)) AS BEGIN INSERT @r VALUES ('ABCDEF', 7, 0x01020304); RETURN; END;",
                "CREATE FUNCTION dbo.obs_mask_reads () RETURNS @r TABLE (c int) AS BEGIN INSERT @r SELECT n + 5 FROM dbo.obs_mask; RETURN; END;",
                "CREATE FUNCTION dbo.obs_mask_inline (@x int) RETURNS TABLE AS RETURN SELECT @x AS x, 7 AS seven;",
            ],
            """
            SELECT p.ordinal FROM dbo.obs_mask AS t CROSS APPLY STRING_SPLIT(t.s, ',', 1) AS p ORDER BY p.ordinal;
            SELECT r.v, r.c, r.b FROM dbo.obs_mask AS t CROSS APPLY dbo.obs_mask_rows(t.n) AS r;
            SELECT j.[type] FROM dbo.obs_mask AS t CROSS APPLY OPENJSON(t.j) AS j;
            SELECT g.value FROM dbo.obs_mask AS t CROSS APPLY GENERATE_SERIES(1, t.n) AS g;
            SELECT f.x, f.seven FROM dbo.obs_mask AS t CROSS APPLY dbo.obs_mask_inline(t.n) AS f;
            SELECT c FROM dbo.obs_mask_reads()
            """);

        // Every column of a built-in or multi-statement function's rows is its mask, its literals and STRING_SPLIT's ordinal included,
        // as many rows as the stored value gives. An inline function is inlined: its literal stays. A body that inserts a value
        // computed from a masked column stores that value's mask, as a table variable does.
        Assert.Equal(
            "ordinal: 0;0 | v,c,b: [xxxx],0,0x30 | type: 0 | value: 0;0 | x,seven: 0,7 | c: 0",
            observed);
    }

    [Fact]
    public async Task Json_query_of_a_masked_column_in_a_for_json_is_its_mask_unquoted()
    {
        var observed = await AsReader(
            ["CREATE TABLE dbo.obs_mask (id int NOT NULL PRIMARY KEY, j nvarchar(100) MASKED WITH (FUNCTION = 'default()') NOT NULL); INSERT dbo.obs_mask VALUES (1, N'{\"a\":{\"b\":1}}');"],
            "SELECT id, json_query(j) AS doc, json_query(j, '$.a') AS a FROM dbo.obs_mask FOR JSON PATH");

        Assert.Equal("JSON_F52E2B61-18A1-11d1-B105-00805F49916B: [[{\"id\":1,\"doc\":xxxx,\"a\":xxxx}]]", observed);
    }

    [Fact]
    public async Task A_scalar_function_that_reads_a_masked_column_returns_its_mask()
    {
        var observed = await AsReader(
            [
                "CREATE TABLE dbo.obs_mask (id int NOT NULL PRIMARY KEY, n int MASKED WITH (FUNCTION = 'default()') NOT NULL); INSERT dbo.obs_mask VALUES (1, 4);",
                "CREATE FUNCTION dbo.obs_mask_inline (@id int) RETURNS int AS BEGIN RETURN (SELECT n FROM dbo.obs_mask WHERE id = @id); END;",
                "CREATE FUNCTION dbo.obs_mask_kept (@id int) RETURNS int WITH INLINE = OFF AS BEGIN DECLARE @v int = (SELECT n FROM dbo.obs_mask WHERE id = @id); RETURN 5; END;",
                "CREATE FUNCTION dbo.obs_mask_filtered (@id int) RETURNS int WITH INLINE = OFF AS BEGIN RETURN (SELECT id FROM dbo.obs_mask WHERE n = 4 AND id = @id); END;",
            ],
            "SELECT dbo.obs_mask_inline(1) * 0 + 5 AS inline_, dbo.obs_mask_kept(1) AS kept, dbo.obs_mask_filtered(1) AS filtered");

        // Even a constant returned after the body reads the column into a variable is masked; a function that only filters on one
        // returns its value, which inference doesn't tell apart (MaskedReaders), so it claims less there.
        Assert.Equal("inline_,kept,filtered: 0,0,1", observed);
    }

    [Fact]
    public async Task A_value_kept_in_a_variable_or_temporary_table_is_computed_on_as_any_value()
    {
        var observed = await AsReader(
            [
                "CREATE TABLE dbo.obs_mask (id int NOT NULL PRIMARY KEY, n int MASKED WITH (FUNCTION = 'default()') NOT NULL); INSERT dbo.obs_mask VALUES (1, 4);",
                "CREATE PROCEDURE dbo.obs_mask_kept AS DECLARE @v int = (SELECT n FROM dbo.obs_mask WHERE id = 1); SELECT @v AS v, @v * 0 + 5 AS five; CREATE TABLE #t (n int); INSERT #t SELECT n FROM dbo.obs_mask; SELECT n, n * 0 + 5 AS five FROM #t;",
            ],
            "EXEC dbo.obs_mask_kept");

        // The mask is what is kept, and what is computed from it later is not masked: inference keeps it masked, which claims less.
        Assert.Equal("v,five: 0,5 | n,five: 0,5", observed);
    }

    [Fact]
    public async Task A_masked_column_of_a_temporary_table_is_not_masked_to_the_session_that_made_it()
    {
        var observed = await AsReader([], "CREATE TABLE #obs_mask (n int MASKED WITH (FUNCTION = 'default()') NULL); INSERT #obs_mask VALUES (42); SELECT n FROM #obs_mask");

        Assert.Equal("n: 42", observed);
    }

    [Fact]
    public async Task A_view_a_procedure_and_their_callers_pass_the_mask_on()
    {
        var observed = await AsReader(
            [
                "CREATE TABLE dbo.obs_mask (id int NOT NULL PRIMARY KEY, n int MASKED WITH (FUNCTION = 'default()') NOT NULL); INSERT dbo.obs_mask VALUES (1, 4);",
                "CREATE VIEW dbo.obs_mask_view AS SELECT id, n FROM dbo.obs_mask;",
                "CREATE PROCEDURE dbo.obs_mask_read AS SELECT d.n FROM (SELECT n FROM dbo.obs_mask_view) AS d;",
            ],
            "SELECT n FROM dbo.obs_mask_view; EXEC dbo.obs_mask_read");

        Assert.Equal("n: 0 | n: 0", observed);
    }

    [Fact]
    public async Task A_user_who_holds_unmask_reads_every_value_and_a_procedure_run_as_another_reads_with_its_permissions()
    {
        string[] setup =
        [
            "CREATE TABLE dbo.obs_mask (id int NOT NULL PRIMARY KEY, n int MASKED WITH (FUNCTION = 'default()') NOT NULL CHECK (n BETWEEN 1 AND 5)); INSERT dbo.obs_mask VALUES (1, 4);",
            "CREATE USER obs_mask_unmasked WITHOUT LOGIN; GRANT SELECT TO obs_mask_unmasked; GRANT EXECUTE TO obs_mask_unmasked; GRANT UNMASK TO obs_mask_unmasked;",
            "CREATE PROCEDURE dbo.obs_mask_as_reader WITH EXECUTE AS 'obs_mask_reader' AS SELECT n FROM dbo.obs_mask;",
            "CREATE PROCEDURE dbo.obs_mask_as_owner WITH EXECUTE AS OWNER AS SELECT n FROM dbo.obs_mask;",
        ];

        Assert.Equal("n: 4 | n: 0", await AsReader(setup, "SELECT n FROM dbo.obs_mask; EXEC dbo.obs_mask_as_reader", "obs_mask_unmasked"));
        Assert.Equal("n: 4", await AsReader(setup, "EXEC dbo.obs_mask_as_owner"));
    }

    [Fact]
    public async Task A_mask_added_or_dropped_later_changes_the_column_s_mask_alone()
    {
        var observed = await AsReader(
            [
                "CREATE TABLE dbo.obs_mask (id int NOT NULL PRIMARY KEY, n int NOT NULL, m varchar(5) MASKED WITH (FUNCTION = 'email()') NULL); INSERT dbo.obs_mask VALUES (1, 4, 'a@b.c');",
                "ALTER TABLE dbo.obs_mask ALTER COLUMN n ADD MASKED WITH (FUNCTION = 'default()'); ALTER TABLE dbo.obs_mask ALTER COLUMN m DROP MASKED;",
            ],
            """
            SELECT n, m FROM dbo.obs_mask;
            SELECT c.name, TYPE_NAME(c.user_type_id) AS type, c.max_length, c.is_nullable, c.is_masked FROM sys.columns AS c WHERE c.object_id = OBJECT_ID('dbo.obs_mask') ORDER BY c.column_id
            """);

        Assert.Equal("n,m: 0,[a@b.c] | name,type,max_length,is_nullable,is_masked: [id],[int],4,false,false;[n],[int],4,false,true;[m],[varchar],5,true,false", observed);
    }
}
