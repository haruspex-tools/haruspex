using System.Text.Json;
using Haruspex.DifferentialTests.Server;

namespace Haruspex.DifferentialTests;

/// <summary>
/// What SQL Server does with a table-valued parameter sent through the mssql driver: which rows it takes, how it matches the
/// columns, and which of the type's constraints and defaults apply to a row sent. What a generated function sends, and what its
/// schema checks before sending, rest on these (decision 0076). Each runs in a database of its own whose types are committed
/// first: a type created in the transaction a call runs in deadlocks the call that sends it (observed, 1205).
/// </summary>
public partial class ParameterObservationTests
{
    /// <summary>Each call, run against <paramref name="definitions"/> (types and procedures in schema <c>obs</c>), created and committed first.</summary>
    private async Task<IReadOnlyList<MssqlDriver.Response>> SentTables(string[] definitions, params MssqlDriver.Call[] calls)
    {
        var token = TestContext.Current.CancellationToken;
        await using var database = await server.ScratchAsync();
        await database.ExecuteAsync("CREATE SCHEMA obs;", token);
        foreach (var definition in definitions)
            await database.ExecuteAsync(definition, token);
        return await MssqlDriver.RunAsync(server, null, [], calls, token, database);
    }

    /// <summary>A call of <paramref name="procedure"/> with its one table-valued parameter <c>@t</c>, or left out when <paramref name="table"/> is null.</summary>
    private static MssqlDriver.Call Sending(string name, string procedure, object? table, bool leftOut = false) =>
        new(name, ParseJson: false, Procedure: procedure, Parameters: leftOut ? null : new Dictionary<string, object?> { ["t"] = table });

    /// <summary>A table of the type <c>obs.<paramref name="type"/></c>, its columns declared as given, in that order.</summary>
    private static MssqlDriver.TableValue Table(string type, (string Name, string Type)[] columns, params object?[][] rows) =>
        new(new MssqlDriver.Table("obs", type, [.. columns.Select(c => Column(c.Name, c.Type))], rows));

    private static MssqlDriver.TableColumn Column(string name, string type) =>
        type.IndexOf('(', StringComparison.Ordinal) is var open and > 0
            ? new(name, type[..open], [.. type[(open + 1)..^1].Split(',').Select(a => a.Trim() == "MAX" ? (object)"MAX" : int.Parse(a, System.Globalization.CultureInfo.InvariantCulture))])
            : new(name, type);

    private const string Pair = "CREATE TYPE obs.pair AS TABLE (k int NOT NULL, d int NULL);";
    private const string ReadPairs = "CREATE PROCEDURE obs.read_pairs @t obs.pair READONLY AS SELECT k, d FROM @t ORDER BY k;";

    private static string Rows(MssqlDriver.Response response) => JsonSerializer.Serialize(Assert.Single(response.Recordsets).Rows);

    [Fact]
    public async Task A_table_valued_parameter_can_be_left_out_or_empty_but_not_null()
    {
        var responses = await SentTables(
            [Pair, ReadPairs],
            Sending("left out", "obs.read_pairs", null, leftOut: true),
            Sending("empty", "obs.read_pairs", Table("pair", [("k", "Int"), ("d", "Int")])),
            Sending("null", "obs.read_pairs", null));

        Assert.Equal(("[]", "[]"), (Rows(responses[0]), Rows(responses[1])));
        Assert.Equal(206, responses[2].Error?.Number);
    }

    [Fact]
    public async Task Columns_are_matched_by_place_not_by_name()
    {
        // Sent as d then k, the first value goes into k, the type's first column: no error, the values swapped.
        var response = Assert.Single(await SentTables([Pair, ReadPairs], Sending("swapped", "obs.read_pairs", Table("pair", [("d", "Int"), ("k", "Int")], [1, 9]))));

        Assert.Null(response.Error);
        Assert.Equal("""[{"k":1,"d":9}]""", Rows(response));
    }

    [Fact]
    public async Task A_column_left_out_fails_the_call()
    {
        var response = Assert.Single(await SentTables([Pair, ReadPairs], Sending("one column", "obs.read_pairs", Table("pair", [("k", "Int")], [1]))));

        Assert.Equal(500, response.Error?.Number);
    }

    [Theory]
    [InlineData("a NOT NULL column with a default", "grade tinyint NOT NULL DEFAULT 1", 515)]
    [InlineData("a nullable column with a default", "grade tinyint NULL DEFAULT 1", null)]
    public async Task A_default_never_applies_to_a_row_a_table_valued_parameter_is_sent(string column, string declared, int? refused)
    {
        // NULL sent for the column is NULL, or refused: the default applies only to a column an INSERT leaves out, and every
        // column of a row sent is given.
        var response = Assert.Single(await SentTables(
            [$"CREATE TYPE obs.graded AS TABLE (tag varchar(5) NOT NULL, {declared});", "CREATE PROCEDURE obs.read_graded @t obs.graded READONLY AS SELECT tag, grade FROM @t;"],
            Sending(column, "obs.read_graded", Table("graded", [("tag", "VarChar(5)"), ("grade", "TinyInt")], ["a", null]))));

        if (refused is { } number)
            Assert.Equal([number, 8061], response.Error?.Preceding);
        else
            Assert.Equal("""[{"tag":"a","grade":null}]""", Rows(response));
    }

    [Theory]
    [InlineData("PRIMARY KEY (k)")]
    [InlineData("UNIQUE (k)")]
    public async Task A_table_valued_parameter_s_key_refuses_a_duplicate_row(string key)
    {
        var response = Assert.Single(await SentTables(
            [$"CREATE TYPE obs.keyed AS TABLE (k int NOT NULL, d int NULL, {key});", "CREATE PROCEDURE obs.read_keyed @t obs.keyed READONLY AS SELECT k, d FROM @t;"],
            Sending("twice", "obs.read_keyed", Table("keyed", [("k", "Int"), ("d", "Int")], [1, 1], [1, 2]))));

        // The driver's own error has no number; the server's come before it: the key's, then the table's (8061).
        Assert.Null(response.Error?.Number);
        Assert.Equal([2627, 8061], response.Error?.Preceding);
    }

    [Fact]
    public async Task A_key_that_ignores_duplicates_drops_a_duplicate_row_a_table_valued_parameter_is_sent()
    {
        var response = Assert.Single(await SentTables(
            ["CREATE TYPE obs.echo AS TABLE (k int NOT NULL PRIMARY KEY WITH (IGNORE_DUP_KEY = ON), d int NULL);", "CREATE PROCEDURE obs.read_echo @t obs.echo READONLY AS SELECT k, d FROM @t;"],
            Sending("twice", "obs.read_echo", Table("echo", [("k", "Int"), ("d", "Int")], [1, 1], [1, 2]))));

        Assert.Null(response.Error);
        Assert.Equal("""[{"k":1,"d":1}]""", Rows(response));
    }

    [Fact]
    public async Task A_check_on_a_table_type_refuses_a_row_that_breaks_it()
    {
        var response = Assert.Single(await SentTables(
            ["CREATE TYPE obs.counted AS TABLE (qty smallint NOT NULL CHECK (qty > 0));", "CREATE PROCEDURE obs.read_counted @t obs.counted READONLY AS SELECT qty FROM @t;"],
            Sending("zero", "obs.read_counted", Table("counted", [("qty", "SmallInt")], [0]))));

        Assert.Equal([547, 8061], response.Error?.Preceding);
    }

    [Fact]
    public async Task A_value_longer_than_its_column_fails_the_call_with_8037()
    {
        // Not cut short, as a scalar parameter sent as max is: the column is declared with the table, which the server refuses.
        var response = Assert.Single(await SentTables(
            ["CREATE TYPE obs.noted AS TABLE (note nvarchar(5) NULL);", "CREATE PROCEDURE obs.read_noted @t obs.noted READONLY AS SELECT note FROM @t;"],
            Sending("long", "obs.read_noted", Table("noted", [("note", "NVarChar(5)")], ["abcdefg"]))));

        Assert.Equal([8037, 8061], response.Error?.Preceding);
    }

    [Theory]
    [InlineData("an identity column", "id int IDENTITY", "Int", 1077)]
    [InlineData("a computed column", "doubled AS v * 2", "Int", 271)]
    [InlineData("a rowversion column", "stamp rowversion", "VarBinary(8)", 273)]
    public async Task A_type_with_a_column_the_server_fills_can_t_be_sent_even_with_no_rows(string kind, string declared, string sent, int refused)
    {
        // Every column must be sent (500 otherwise), and this one can't be, not even in an empty table; only left out does the call
        // run. Left out first: the refusal ends the transaction the calls share.
        var responses = await SentTables(
            [$"CREATE TYPE obs.filled AS TABLE (v int NULL, {declared});", "CREATE PROCEDURE obs.count_filled @t obs.filled READONLY AS SELECT count(*) AS n FROM @t;"],
            Sending("left out", "obs.count_filled", null, leftOut: true),
            Sending(kind, "obs.count_filled", Table("filled", [("v", "Int"), ("other", sent)])));

        Assert.Equal("""[{"n":0}]""", Rows(responses[0]));
        Assert.Equal([refused, 8061], responses[1].Error?.Preceding);
    }

    [Fact]
    public async Task A_table_type_s_column_takes_the_database_s_collation()
    {
        var response = Assert.Single(await SentTables(
            ["CREATE TYPE obs.tagged AS TABLE (tag varchar(5) NOT NULL);"],
            new MssqlDriver.Call("collation", ParseJson: false, Batch: """
                SELECT CAST(CASE WHEN c.collation_name = (SELECT d.collation_name FROM sys.databases AS d WHERE d.name = DB_NAME()) THEN 1 ELSE 0 END AS bit) AS same
                FROM sys.table_types AS t JOIN sys.columns AS c ON c.object_id = t.type_table_object_id
                WHERE t.name = N'tagged';
                """)));

        Assert.Equal("""[{"same":true}]""", Rows(response));
    }
}
