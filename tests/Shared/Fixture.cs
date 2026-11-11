using Haruspex.Cli;
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

    private Fixture(string folder)
    {
        Folder = folder;
        Name = Path.GetFileName(folder);
        project = new(() => SqlProject.Load(Database));
        ir = new(InferEveryProcedure);
        IsProject = Config is { } config && ConfigReader.Read(config).Config?.Source is not null;
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

    /// <summary>The fixture's database, read once.</summary>
    public SqlProject Project => project.Value;

    /// <summary>
    /// The IR of every procedure, inferred once, with the fixture's config file applied when it has one (it declares
    /// results inference can't see, such as those of dynamic SQL). Fails when the database has diagnostics.
    /// </summary>
    public IrDocument Ir => ir.Value;

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
        if (IsProject && (Seed is not null || Runs is not null))
            throw new InvalidOperationException($"The fixture {Name} is a project for the CLI (its config file names a source), so it is not deployed: it has no seed.sql or runs.json.");

        if (missing.Count > 0)
            throw new InvalidOperationException($"The fixture {Name} lacks {string.Join(", ", missing)} (tests/fixtures/README.md says what each part is).");
    }

    private IrDocument InferEveryProcedure()
    {
        if (Project.Diagnostics.Count > 0)
            throw new InvalidOperationException($"The fixture {Name} has diagnostics: {string.Join("; ", Project.Diagnostics)}");

        if (Config is null)
            return Project.Infer(Project.Procedures);
        var read = ConfigReader.Read(Config);
        var applied = Project.Infer(Project.Procedures, read.Config ?? throw new InvalidOperationException(string.Join("; ", read.Problems)));
        return applied.Document ?? throw new InvalidOperationException(string.Join("; ", applied.Problems));
    }
}
