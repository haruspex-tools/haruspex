using Haruspex.Cli;
using Haruspex.Diagnostics;
using Haruspex.Ir;

namespace Haruspex.TestSupport;

/// <summary>
/// One fixture: an invented database in a folder of its own under <c>tests/fixtures</c>, holding what haruspex reads and
/// what it must produce, and, when the differential tests deploy it, its seed and its runs. The layout is described in
/// <c>tests/fixtures/README.md</c>.
/// </summary>
/// <remarks>
/// Fixtures are found by listing the folder, never named in a list: every test that needs "every fixture" asks
/// <see cref="All"/>, so the snapshot, emit-check, seed, coverage and acceptance tests can't disagree about which fixtures
/// exist. A folder that is missing a part its kind needs fails discovery, and with it every test that uses a fixture,
/// rather than being skipped.
/// </remarks>
internal sealed class Fixture
{
    private readonly Lazy<SqlProject> project;
    private readonly Lazy<IrDocument> ir;
    private readonly Lazy<IrDocument?> queryIr;

    /// <summary>The suffix of a committed IR snapshot, beside which is the Zod snapshot generated from it.</summary>
    private const string IrSnapshot = ".ir.verified.json";

    /// <summary>The fixture's config file as read, or null when it has none.</summary>
    private readonly Outcome<HaruspexConfig>? configRead;

    private Fixture(string folder)
    {
        Folder = folder;
        Name = Path.GetFileName(folder);
        project = new(() => SqlProject.Load(Database, configRead?.OrDefault()?.Collation, configRead?.OrDefault()?.Descriptions));
        ir = new(InferEveryProcedure);
        queryIr = new(() => Queries is null ? null : InferQuery.Module(Folder, Database, QueryFolder));
        configRead = Config is { } config ? ConfigReader.Read(config) : null;
        IsProject = configRead?.OrDefault()?.Source is not null;
        IsSplit = configRead?.OrDefault()?.Split is not null;
    }

    /// <summary>Every fixture, in ordinal order of name.</summary>
    public static IReadOnlyList<Fixture> All { get; } = Discover();

    /// <summary>The fixture's name: its folder's name.</summary>
    public string Name { get; }

    /// <summary>The fixture's folder.</summary>
    public string Folder { get; }

    /// <summary>The scripts haruspex reads (tables, views, functions, procedures, triggers), and nothing else.</summary>
    public string Database => Path.Combine(Folder, "database");

    /// <summary>What haruspex must produce from <see cref="Database"/>: the reviewed snapshots.</summary>
    public string Expected => Path.Combine(Folder, "expected");

    /// <summary>
    /// The committed IR files in <see cref="Expected"/>, each with the Zod snapshot generated from it: one pair per procedure,
    /// or for a project the pair the CLI writes.
    /// </summary>
    /// <remarks>
    /// A project whose config file splits its output has no such pair: its files are a module's each, which
    /// <c>SplitThroughTheFileTests</c> emits from its IR file instead.
    /// </remarks>
    public IEnumerable<(string Ir, string Zod)> Snapshots =>
        IsSplit ? [] : Directory.EnumerateFiles(Expected, "*" + IrSnapshot)
            .Order(StringComparer.Ordinal)
            .Select(ir => (ir, ir[..^IrSnapshot.Length] + ".zod.verified.ts"));

    /// <summary>
    /// The committed IR files in <see cref="Expected"/>, each with the JSON Schema snapshot generated from it, as
    /// <see cref="Snapshots"/> pairs each with its Zod.
    /// </summary>
    public IEnumerable<(string Ir, string JsonSchema)> JsonSchemaSnapshots =>
        Snapshots.Select(s => (s.Ir, s.Ir[..^IrSnapshot.Length] + ".jsonschema.verified.json"));

    /// <summary>
    /// The folder of a deployed fixture's query files, relative to the fixture's folder: the application's own SQL, typed against
    /// its database as <c>"queries": "sql"</c> in a config file there would type it.
    /// </summary>
    public const string QueryFolder = "sql";

    /// <summary>The fixture's query files (<see cref="QueryFolder"/>), or null when it has none.</summary>
    public string? Queries => Directory.Exists(Path.Combine(Folder, QueryFolder)) ? Path.Combine(Folder, QueryFolder) : null;

    /// <summary>The fixture's <c>haruspex.json</c>, or null when it has none.</summary>
    public string? Config => File.Exists(Path.Combine(Folder, HaruspexConfig.FileName)) ? Path.Combine(Folder, HaruspexConfig.FileName) : null;

    /// <summary>The data the differential tests insert after deploying the fixture, or null when it isn't deployed.</summary>
    public string? Seed => File.Exists(Path.Combine(Folder, "seed.sql")) ? Path.Combine(Folder, "seed.sql") : null;

    /// <summary>The fixture's runs against SQL Server (<c>runs.json</c>), or null when it isn't deployed.</summary>
    public string? Runs => File.Exists(Path.Combine(Folder, "runs.json")) ? Path.Combine(Folder, "runs.json") : null;

    /// <summary>Whether the differential tests deploy, seed and run the fixture.</summary>
    public bool IsDeployed => Seed is not null;

    /// <summary>
    /// Whether the fixture is a whole project for the CLI: its config file names the <c>source</c> folder, chooses the
    /// procedures and names them, so its expected output is the CLI's two files rather than one snapshot per procedure.
    /// </summary>
    public bool IsProject { get; }

    /// <summary>Whether the fixture's config file splits its output into modules (<c>split</c>), a project's files then being a module's each.</summary>
    public bool IsSplit { get; }

    /// <summary>The fixture's database, read once.</summary>
    public SqlProject Project => project.Value;

    /// <summary>
    /// The IR of every procedure, inferred once, with the fixture's config file applied when it has one (it declares
    /// results inference can't see, such as those of dynamic SQL). Fails when the database has diagnostics.
    /// </summary>
    public IrDocument Ir => ir.Value;

    /// <summary>
    /// The module of the fixture's query files, inferred once, with no procedure, so that a query's id and the path its function
    /// names are relative to the fixture's folder (<c>sql/getOrderLines.sql</c>), as an application's are to its config file; null
    /// when it has none. Fails when the database or a query file has a problem.
    /// </summary>
    public IrDocument? QueryIr => queryIr.Value;

    /// <summary>The fixture with this name.</summary>
    public static Fixture Named(string name) =>
        All.SingleOrDefault(f => f.Name == name) ?? throw new ArgumentException($"There is no fixture {name} in {RepositoryPaths.Fixtures}.", nameof(name));

    /// <inheritdoc/>
    public override string ToString() => Name;

    private static List<Fixture> Discover()
    {
        var fixtures = Directory.EnumerateDirectories(RepositoryPaths.Fixtures)
            .Order(StringComparer.Ordinal)
            .Select(folder => new Fixture(folder))
            .ToList();
        foreach (var fixture in fixtures)
            fixture.Check();
        return fixtures;
    }

    /// <summary>Fails discovery when the folder lacks a part its kind needs, so a fixture can't be half added.</summary>
    private void Check()
    {
        // A config file that doesn't read leaves the fixture's kind unknown, so it is reported before any missing part.
        if (configRead is Failed<HaruspexConfig> broken)
            throw new InvalidOperationException($"The fixture {Name}'s {HaruspexConfig.FileName} can't be read: {string.Join("; ", broken.Diagnostics)}");

        var missing = new List<string>();
        if (!Directory.Exists(Database))
            missing.Add("database/");
        if (!Directory.Exists(Expected))
            missing.Add("expected/");
        if (!File.Exists(Path.Combine(Folder, "README.md")))
            missing.Add("README.md");

        // A fixture that is not a project is checked per procedure against the server, so it is deployed: a seed without
        // runs, or runs without a seed, would leave half of that unchecked.
        if (!IsProject && Seed is null)
            missing.Add("seed.sql");
        if (!IsProject && Runs is null)
            missing.Add("runs.json");
        // A query file is held to the server by its runs, so only a deployed fixture may have one; a project names its own.
        if (Queries is not null && Seed is null)
            throw new InvalidOperationException($"The fixture {Name} has query files in {QueryFolder}/ but isn't deployed, so they would never run.");
        if (IsProject && (Seed is not null || Runs is not null))
            throw new InvalidOperationException($"The fixture {Name} is a project for the CLI (its config file names a source), so it is not deployed: it has no seed.sql or runs.json.");

        if (missing.Count > 0)
            throw new InvalidOperationException($"The fixture {Name} lacks {string.Join(", ", missing)} (tests/fixtures/README.md says what each part is).");
    }

    private IrDocument InferEveryProcedure()
    {
        if (Project.Diagnostics.Count > 0)
            throw new InvalidOperationException($"The fixture {Name} has diagnostics: {string.Join("; ", Project.Diagnostics)}");

        if (configRead is null)
            return Project.Infer(Project.Procedures).Expected();
        var applied = Project.Infer(Project.Procedures, configRead.Expected());
        return applied.Expected();
    }
}
