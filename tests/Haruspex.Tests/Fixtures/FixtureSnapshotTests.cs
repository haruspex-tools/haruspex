using Haruspex.Cli;
using Haruspex.Emit.Zod;
using Haruspex.Ir;
using Haruspex.Tests.Snapshotting;

namespace Haruspex.Tests.Fixtures;

/// <summary>
/// What haruspex must produce from each fixture, kept in the fixture's <c>expected/</c> folder: for a fixture checked per
/// procedure, one snapshot of the IR and one of the Zod per procedure; for a project for the CLI, the two files
/// <c>haruspex generate</c> writes with its config. A snapshot is a claim about what SQL Server returns, so a change to one
/// is reviewed against the engine's behaviour, never accepted unread.
/// </summary>
public class FixtureSnapshotTests
{
    private static readonly IReadOnlyList<Fixture> Fixtures = [.. Fixture.All.Where(f => !f.IsProject)];

    /// <summary>The two files <c>haruspex generate</c> writes, which a project fixture's <c>expected/</c> holds.</summary>
    private static readonly (string Written, string Snapshot, string Extension)[] ProjectOutputs =
        [("haruspex.ir.json", "haruspex.ir", "json"), ("haruspex.zod.ts", "haruspex.zod", "ts")];

    /// <summary>Every fixture procedure, with its fixture. Procedure ids are unique across fixtures.</summary>
    private static readonly Dictionary<string, Fixture> FixtureOf =
        Fixtures.SelectMany(f => f.Project.Procedures.Select(p => (Procedure: p.ToString(), Fixture: f)))
            .ToDictionary(p => p.Procedure, p => p.Fixture, StringComparer.Ordinal);

    public static TheoryData<string> Procedures { get; } = [.. FixtureOf.Keys];

    public static TheoryData<string> FixtureNames { get; } = [.. Fixtures.Select(f => f.Name)];

    public static TheoryData<string> ProjectNames { get; } = [.. Fixture.All.Where(f => f.IsProject).Select(f => f.Name)];

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void The_fixture_loads_without_diagnostics(string fixture) => Assert.Empty(Fixture.Named(fixture).Project.Diagnostics);

    // Each procedure has its two snapshots, and each snapshot a procedure: a renamed or deleted procedure leaves no
    // reviewed file behind that nothing checks.
    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void Every_fixture_procedure_has_snapshots(string fixture)
    {
        var expected = Fixture.Named(fixture).Expected;
        var procedures = Fixture.Named(fixture).Project.Procedures.Select(p => p.ToString()).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(procedures, Snapshots(expected, ".ir.verified.json"));
        Assert.Equal(procedures, Snapshots(expected, ".zod.verified.ts"));
        Assert.DoesNotContain(Reviewed(expected), f => !f.EndsWith(".ir.verified.json", StringComparison.Ordinal) && !f.EndsWith(".zod.verified.ts", StringComparison.Ordinal));
    }

    // A project is run as a user runs it, through the CLI in its folder with its config, and its expected/ holds what that
    // writes and nothing else.
    [Theory]
    [MemberData(nameof(ProjectNames))]
    public void Each_project_generates_its_reviewed_output(string fixture)
    {
        var project = Fixture.Named(fixture);
        using var outDir = new TempDirectory();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = HaruspexCli.Run(["generate", "--out", outDir.Path], output, error, project.Folder);

        Assert.True(exitCode == 0, error.ToString());
        foreach (var (written, snapshot, extension) in ProjectOutputs)
            Snapshot.Match(File.ReadAllText(Path.Combine(outDir.Path, written)), project.Expected, snapshot, extension);
        Assert.Equal(
            ProjectOutputs.Select(o => $"{o.Snapshot}.verified.{o.Extension}").Order(StringComparer.Ordinal),
            Reviewed(project.Expected));
    }

    // A snapshot is named for what it holds, the IR or the Zod of one procedure, not for the test that checks it, so that
    // renaming a test does not move 276 reviewed files.
    [Theory]
    [MemberData(nameof(Procedures))]
    public void Each_procedure_infers_the_reviewed_ir(string procedure) =>
        Snapshot.Match(IrJsonWriter.Write(Infer(procedure)), FixtureOf[procedure].Expected, $"{procedure}.ir", "json");

    // The Zod is emitted from the IR as its file gives it back, written and read again, so it is what a consumer of the file
    // would generate; ZodThroughTheFileTests does the same from the committed IR files themselves.
    [Theory]
    [MemberData(nameof(Procedures))]
    public void Each_procedure_emits_the_reviewed_zod(string procedure) =>
        Snapshot.Match(ZodEmitter.Emit(IrJsonReader.Read(IrJsonWriter.Write(Infer(procedure)))), FixtureOf[procedure].Expected, $"{procedure}.zod", "ts");

    /// <summary>The names of the files in a fixture's <c>expected/</c>, leaving out any received file a failing run wrote.</summary>
    private static List<string> Reviewed(string folder) =>
        [.. Directory.EnumerateFiles(folder).Select(f => Path.GetFileName(f)).Where(f => !f.Contains(".received.", StringComparison.Ordinal)).Order(StringComparer.Ordinal)];

    private static List<string> Snapshots(string folder, string suffix) =>
        [.. Directory.EnumerateFiles(folder, "*" + suffix).Select(f => Path.GetFileName(f)[..^suffix.Length]).Order(StringComparer.Ordinal)];

    private static IrDocument Infer(string procedure)
    {
        var fixture = FixtureOf[procedure];
        var project = fixture.Project;
        var selection = project.Select([ObjectPattern.Parse(procedure)]);
        Assert.Empty(selection.Unmatched);

        // A fixture's haruspex.json declares results inference can't see; a declaration a procedure doesn't use is only a
        // warning, which a snapshot of one procedure ignores.
        if (fixture.Config is null)
            return project.Infer(selection.Procedures);
        var read = ConfigReader.Read(fixture.Config);
        var applied = project.Infer(selection.Procedures, read.Config!);
        return applied.Document ?? throw new InvalidOperationException(string.Join("; ", applied.Problems));
    }
}
