using System.Text;
using System.Text.Json;
using Haruspex.Cli;
using Haruspex.DifferentialTests.Server;
using Haruspex.Ir;

namespace Haruspex.DifferentialTests;

/// <summary>
/// A parameter declared to hold JSON of a schema, through the generated function: the caller passes the value, which is checked
/// against the schema, serialised with <c>JSON.stringify</c>, and the text checked against the parameter's type before anything
/// is sent (docs/decisions/0143-a-parameter-s-json-may-be-declared.md).
/// </summary>
public partial class MssqlFunctionTests
{
    private const string Order = """
        { "type": "object", "required": ["mode"], "properties": {
            "mode": { "enum": ["fast", "standard"] },
            "note": { "type": "string" } } }
        """;

    private const string ReadOrder = """
        CREATE PROCEDURE outlet.read_order @order nvarchar(60), @label varchar(60) = NULL
        AS SELECT json_value(@order, '$.mode') AS mode, @order AS sent, json_value(@label, '$.note') AS label;
        """;

    /// <summary>The database module of <see cref="ReadOrder"/>, its <c>@order</c> and <c>@label</c> declared to hold JSON of <see cref="Order"/>.</summary>
    private static IrDocument Declared()
    {
        using var dir = new TempDirectory();
        dir.File("db/schema.sql", "CREATE SCHEMA outlet;");
        dir.File("db/read_order.sql", ReadOrder);
        dir.File("schemas/order.schema.json", Order);
        var project = SqlProject.Load(Path.Combine(dir.Path, "db"));
        Assert.Empty(project.Diagnostics);
        var config = ConfigReader.Read(
            Encoding.UTF8.GetBytes("""{ "parameters": { "outlet.read_order.@order": { "json": "schemas/order.schema.json" }, "outlet.read_order.@label": { "json": "schemas/order.schema.json" } } }"""),
            Path.Combine(dir.Path, "haruspex.json"),
            dir.Path).Expected();
        return project.Infer(project.Procedures, config).Expected();
    }

    [Fact]
    public async Task A_declared_parameter_takes_the_value_and_sends_its_json_text()
    {
        var response = Assert.Single(await Run(Declared(), [ReadOrder], new GeneratedFunctions.Call("call", "readOrder", Params(("order", Params(("mode", "fast"), ("note", "x")))))));

        Assert.Null(response.Error);
        Assert.Equal("""{"mode":"fast","note":"x"}""", response.Value!.Value[0].GetProperty("sent").GetString());
    }

    [Fact]
    public async Task A_declared_parameter_takes_null_and_sends_null()
    {
        var response = Assert.Single(await Run(Declared(), [ReadOrder], new GeneratedFunctions.Call("call", "readOrder", Params(("order", null)))));

        Assert.Null(response.Error);
        Assert.Equal("""[{"mode":null,"sent":null,"label":null}]""", JsonSerializer.Serialize(response.Value));
    }

    [Fact]
    public async Task A_value_its_schema_refuses_is_refused_before_the_connection_is_touched()
    {
        var response = Assert.Single(await Run(Declared(), [ReadOrder], new GeneratedFunctions.Call("wrong", "readOrder", Params(("order", Params(("mode", "slow")))), Db: "untouchable")));

        var error = response.Error!;
        Assert.Equal(("HaruspexParameterError", "outlet.read_order"), (error.Name, error.Procedure));
        Assert.Equal(["order.mode"], error.Issues.Select(i => string.Join(".", i.Path)));
    }

    [Fact]
    public async Task Json_text_longer_than_the_parameter_s_type_is_refused_since_the_server_would_cut_it_short()
    {
        // {"mode":"fast","note":"…"} with 36 characters of note is 61: one more than nvarchar(60) keeps, which would cut it into text that isn't JSON.
        var response = Assert.Single(await Run(Declared(), [ReadOrder], new GeneratedFunctions.Call("long", "readOrder", Params(("order", Params(("mode", "fast"), ("note", new string('n', 36))))), Db: "untouchable")));

        var error = response.Error!;
        Assert.True(error.Name == "HaruspexParameterError", error.Message);
        Assert.Equal(["order"], error.Issues.Select(i => string.Join(".", i.Path)));
    }

    [Fact]
    public async Task A_varchar_parameter_s_json_text_is_checked_as_any_text_it_is_sent()
    {
        // The varchar check is the one any text sent there gets: its length, and a character the code page lacks is replaced, as
        // the generated schema's note says when the collation isn't declared: ā through the driver into code page 1252 is ?
        // (observed). A replaced character inside a JSON string leaves JSON.
        var response = Assert.Single(await Run(Declared(), [ReadOrder], new GeneratedFunctions.Call("code page", "readOrder", Params(("order", null), ("label", Params(("mode", "fast"), ("note", "ā")))))));

        Assert.Null(response.Error);
        Assert.Equal("?", response.Value!.Value[0].GetProperty("label").GetString());
    }
}
