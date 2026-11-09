using System.Text;
using System.Text.Json;
using Haruspex.Cli;
using Haruspex.DifferentialTests.Server;
using Haruspex.Emit.Zod;
using Haruspex.TestSupport;

namespace Haruspex.DifferentialTests;

/// <summary>
/// Overrides stay runtime-checked (haruspex-design.md, "Overrides"): an override is a claim the generated validator
/// still checks, so a wrong one fails loudly on real data rather than casting it. Here the config file of the checks
/// fixture makes two wrong claims about <c>chk.list_tickets</c>, which SQL Server's real output breaks: a note declared
/// never null (ticket 3 has none), and a status set declared exact (ticket 3's is <c>'CLOSED'</c>, which passes the
/// case-insensitive CHECK). The same output passes the schema generated without the override.
/// </summary>
public class OverrideAcceptanceTests(SqlServerFixture server)
{
    public static TheoryData<string, string, bool> Claims { get; } = new()
    {
        { "{}", "", true },
        { """{ "procedures": { "chk.list_tickets": { "paths": { "[].note": { "nullable": false, "why": "wrongly" } } } } }""", "note", false },
        { """{ "procedures": { "chk.list_tickets": { "paths": { "[].status": { "values": "exact", "why": "wrongly" } } } } }""", "status", false },
    };

    [Theory]
    [MemberData(nameof(Claims))]
    public async Task A_wrong_override_is_rejected_by_the_generated_zod(string config, string rejectedKey, bool accepted)
    {
        server.Require();
        EmitCheckPackage.RequireNode(Paths.EmitCheck, "the generated Zod was not run against real output");

        var project = SqlProject.Load(Paths.Fixture("checks"));
        var read = ConfigReader.Read(Encoding.UTF8.GetBytes(config), "haruspex.json", Paths.Fixture("checks"));
        Assert.Empty(read.Problems);
        var selected = project.Procedures.Where(p => p.ToString() == "chk.list_tickets").ToList();
        var applied = project.Infer(selected, read.Config!);
        var document = applied.Document ?? throw new InvalidOperationException(string.Join("; ", applied.Problems));
        var procedure = Assert.Single(document.Objects);
        var run = Runs.All.First(r => r.Procedure == "chk.list_tickets" && r.Setup is null);
        var observed = await server.ObserveAsync(run);

        var folder = Path.Combine(Paths.EmitCheck, "accept", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "checks.zod.ts"), ZodEmitter.Emit(document));
            using (var cases = File.Create(Path.Combine(folder, "cases.json")))
            using (var json = new Utf8JsonWriter(cases))
            {
                json.WriteStartObject();
                json.WriteString("module", "checks.zod.ts");
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
