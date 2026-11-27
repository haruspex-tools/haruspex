using System.Security.Cryptography;
using System.Text;
using Haruspex.Cli;
using Haruspex.Diagnostics;

namespace Haruspex.Tests.Config;

/// <summary>
/// An external REST endpoint declared to send a response of a schema (<c>endpoints[]</c>), read from the config file into the IR:
/// its method, its URL, the schema file and the files it reaches. What the inference does with it is in the Inference tests'
/// <c>RestEndpointTests</c>. Why: docs/decisions/0140-a-rest-response-is-typed-from-a-schema-declared-for-its-endpoint.md.
/// </summary>
public class EndpointDeclarationTests
{
    private const string Plan = """{ "type": "object", "properties": { "name": { "type": ["string", "null"] } } }""";

    private static Outcome<HaruspexConfig> Read(TempDirectory dir, string endpoints) =>
        ConfigReader.Read(Encoding.UTF8.GetBytes($$"""{ "endpoints": [{{endpoints}}] }"""), Path.Combine(dir.Path, "haruspex.json"), dir.Path);

    private static string[] Problems(TempDirectory dir, string endpoints) =>
        [.. Read(dir, endpoints).Diagnostics.Select(d => d.ToString().Replace(DiagnosticFormatter.PathAsShown(dir.Path) + "/", "", StringComparison.Ordinal))];

    [Fact]
    public void A_declaration_records_its_method_url_schema_and_each_file_s_hash()
    {
        using var dir = new TempDirectory();
        dir.File("schemas/plan.json", """{ "$defs": { "plan": { "$ref": "name.json" } } }""");
        dir.File("schemas/name.json", Plan);

        var declared = Assert.Single(Read(dir, """{ "url": "https://billing.example/plans/{id}", "method": "GET", "response": "schemas/plan.json#/$defs/plan" }""").Expected().Endpoints);

        Assert.Equal(("GET", "https://billing.example/plans/{id}", "schemas/plan.json", "/$defs/plan"), (declared.Record.Method, declared.Record.Address, declared.Record.Schema, declared.Record.Pointer));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Plan))), declared.Record.Files["schemas/name.json"]);
        Assert.Equal(2, declared.Record.Files.Count);
    }

    [Fact]
    public void A_response_may_allow_null_at_its_top_which_never_arrives()
    {
        // A column's JSON may not (HAR3115); a body that is JSON null fails the call (11558).
        using var dir = new TempDirectory();
        dir.File("plan.json", """{ "type": ["object", "null"] }""");

        Assert.Empty(Problems(dir, """{ "url": "https://billing.example/plans", "method": "GET", "response": "plan.json" }"""));
    }

    [Theory]
    [InlineData("""{ "url": "https://billing.example/plans", "method": "GET" }""", "error HAR3012: endpoints[0]: an endpoint needs \"response\"")]
    [InlineData("""{ "url": "https://billing.example/plans", "method": "get", "response": "plan.json" }""", "error HAR3005: endpoints[0].method: must be \"GET\" or \"POST\" or \"PUT\" or \"PATCH\" or \"DELETE\", not \"get\".")]
    [InlineData("""{ "url": "https://billing.example/plans", "method": "GET", "response": "plan.json" }, { "url": "https://billing.example/plans", "method": "GET", "response": "plan.json" }""", "error HAR3003: endpoints[1]: declares GET https://billing.example/plans again")]
    [InlineData("""{ "url": "https://billing.example/plans", "method": "GET", "response": "plan.json", "status": 200 }""", "error HAR3004: endpoints[0].status")]
    public void A_declaration_that_is_not_one_is_refused(string endpoints, string problem)
    {
        using var dir = new TempDirectory();
        dir.File("plan.json", Plan);

        Assert.Contains(Problems(dir, endpoints), p => p.Contains(problem, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("http://billing.example/plans", "is not an https URL")]
    [InlineData("https://{tenant}.example/plans", "in its host")]
    [InlineData("https://billing.example/plans/{id}{part}", "side by side")]
    [InlineData("https://billing.example/plans/{id", "doesn't close")]
    public void A_url_the_inference_can_t_read_stops_the_run(string address, string problem)
    {
        using var dir = new TempDirectory();
        dir.File("plan.json", Plan);
        dir.File("db/p.sql", "CREATE PROCEDURE dbo.p AS SELECT 1 AS a;");
        var config = Read(dir, $$"""{ "url": "{{address}}", "method": "GET", "response": "plan.json" }""").Expected();
        var project = SqlProject.Load(Path.Combine(dir.Path, "db"));

        var failed = Assert.IsType<Failed<Haruspex.Ir.IrDocument>>(project.Infer(project.Procedures, config));
        var diagnostic = Assert.Single(failed.Diagnostics);
        Assert.Equal("HAR3006", diagnostic.Code.ToString());
        Assert.Contains(problem, diagnostic.Message, StringComparison.Ordinal);
    }
}
