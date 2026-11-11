using System.Text;
using System.Text.Json;
using Haruspex.Cli;
using Haruspex.DifferentialTests.Server;
using Haruspex.Emit.Zod;
using Haruspex.TestSupport;

namespace Haruspex.DifferentialTests;

/// <summary>
/// Declared results stay runtime-checked (slice 6b): a declaration is a claim the generated validator checks, so a wrong
/// one fails loudly on real data. <c>calls.search_batches</c> runs dynamic SQL, so its results come only from a declaration.
/// The right one (the fixture's own haruspex.json) accepts what SQL Server returns; one declaring <c>weight_g</c> never
/// null is refused by batch 11, which has no weight; one declaring <c>product</c> a number is refused by every row.
/// </summary>
public class DeclarationAcceptanceTests(SqlServerFixture server)
{
    private const string Right = """
        "batch_id": { "type": "integer", "format": "int32" },
        "product": { "type": "string" },
        "weight_g": { "type": ["integer", "null"], "format": "int32" }
        """;

    public static TheoryData<string, string, bool> Declarations { get; } = new()
    {
        { Right, "", true },
        { Right.Replace("""["integer", "null"]""", "\"integer\"", StringComparison.Ordinal), "weight_g", false },
        { Right.Replace("""{ "type": "string" }""", """{ "type": "number" }""", StringComparison.Ordinal), "product", false },
    };

    [Theory]
    [MemberData(nameof(Declarations))]
    public async Task A_wrong_declaration_is_rejected_by_the_generated_zod(string columns, string rejectedKey, bool accepted)
    {
        server.Require();
        EmitCheckPackage.RequireNode(Paths.EmitCheck, "the generated Zod was not run against real output");

        var config = $$"""
            { "procedures": { "calls.search_batches": {
              "results": { "rowset": { "type": "object", "properties": { {{columns}} } } },
              "why": "dynamic SQL"
            } } }
            """;
        var project = SqlProject.Load(Paths.Fixture("calls"));
        var read = ConfigReader.Read(Encoding.UTF8.GetBytes(config), "haruspex.json", Paths.Fixture("calls"));
        Assert.Empty(read.Problems);
        var selected = project.Procedures.Where(p => p.ToString() == "calls.search_batches").ToList();
        var applied = project.Infer(selected, read.Config!);
        var document = applied.Document ?? throw new InvalidOperationException(string.Join("; ", applied.Problems));
        var procedure = Assert.Single(document.Objects);
        var run = Runs.Named("calls: search_batches");
        var observed = await server.ObserveAsync(run);

        var folder = Path.Combine(Paths.EmitCheck, "accept", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "calls.zod.ts"), ZodEmitter.Emit(document), TestContext.Current.CancellationToken);
            using (var cases = File.Create(Path.Combine(folder, "cases.json")))
            using (var json = new Utf8JsonWriter(cases))
            {
                json.WriteStartObject();
                json.WriteString("module", "calls.zod.ts");
                json.WriteStartArray("cases");
                json.WriteStartObject();
                json.WriteString("run", run.Name);
                json.WriteString("schema", procedure.Name + "Schema");
                json.WritePropertyName("value");
                ZodAcceptanceTests.WriteResult(json, procedure.Results![0], observed[0]);
                json.WriteEndObject();
                json.WriteEndArray();
                json.WriteEndObject();
            }

            EmitCheckPackage.EnsureInstalled(Paths.EmitCheck);
            var passed = EmitCheckPackage.TryRun("node", [Path.Combine("src", "accept.mjs"), folder], Paths.EmitCheck, out var output, out _);

            Assert.True(passed == accepted, output);
            if (!accepted)
                Assert.Contains($".{rejectedKey}: ", output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
