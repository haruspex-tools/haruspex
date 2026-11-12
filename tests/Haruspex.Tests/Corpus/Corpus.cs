using System.Xml.Linq;
using Haruspex.SqlServer.Inference;
using Haruspex.SqlServer.Sources;

namespace Haruspex.Tests.Corpus;

/// <summary>
/// One of Microsoft's sample databases as the corpus test reads it: which of its scripts haruspex reads, how, and which it
/// leaves out and why. Nothing is left out without a reason, and the figures page lists every file left out.
/// </summary>
/// <param name="Name">The database's name, as Microsoft gives it.</param>
/// <param name="Folder">The project's folder, as an upstream path below the vendored root.</param>
/// <param name="Scripts">The scripts read, relative to <paramref name="Folder"/>.</param>
/// <param name="LeftOut">The folder's other scripts, in groups that share a reason.</param>
/// <param name="HowRead">What the corpus is and how it is read, for the figures page.</param>
internal sealed record Corpus(string Name, string Folder, IReadOnlyList<string> Scripts, IReadOnlyList<Corpus.Omission> LeftOut, string HowRead)
{
    /// <summary>The corpora, in the order the figures page shows them.</summary>
    public static IReadOnlyList<Corpus> All { get; } = [AdventureWorks(), WideWorldImporters()];

    /// <summary>
    /// What haruspex gives on each corpus, in <see cref="All"/>'s order: run once, when first asked for, and shared by every
    /// test that reads it, because a run takes seconds.
    /// </summary>
    public static IReadOnlyList<CorpusRun> Runs => SharedRuns.Value;

    private static readonly Lazy<IReadOnlyList<CorpusRun>> SharedRuns = new(() => [.. All.Select(c => c.Run())]);

    /// <summary>The project's folder on disk.</summary>
    public string ProjectFolder => CorpusManifest.Current.PathOf(Folder);

    /// <summary>
    /// Runs haruspex over the scripts read: every procedure in every schema, inferred with no config file. Names for
    /// generated code are not settled, since they don't change what is typed (WideWorldImporters' would need a config file:
    /// two of its schemas have a procedure of the same name).
    /// </summary>
    public CorpusRun Run()
    {
        var read = SqlScriptFolder.Read(ProjectFolder, Scripts);
        var document = IrInference.Infer(read.Model, [.. read.Model.Procedures.Select(p => p.Name)]);
        return new CorpusRun(this, read.Diagnostics, document);
    }

    /// <summary>AdventureWorks' install script for SQL Server: one file of <c>GO</c> batches, written for sqlcmd.</summary>
    private static Corpus AdventureWorks() => new(
        "AdventureWorks",
        "samples/databases/adventure-works/oltp-install-script",
        ["instawdb.sql"],
        [],
        "One install script, `instawdb.sql`, written for sqlcmd: it names the database with `:setvar` and `$(DatabaseName)`, " +
        "creates it, and creates the schema in `GO` batches. The sqlcmd variables are replaced as sqlcmd replaces them, and the " +
        "definitions are read. The other statements define nothing and are passed over: `USE`, `CREATE DATABASE`, settings, " +
        "and the `BULK INSERT` of the CSV data beside the script, which is not vendored.");

    /// <summary>
    /// WideWorldImporters as an SSDT project. haruspex can't read a <c>.sqlproj</c> yet, so the scripts read are the ones the
    /// project builds, taken from its <c>Build</c> items here.
    /// </summary>
    private static Corpus WideWorldImporters()
    {
        const string folder = "samples/databases/wide-world-importers/wwi-ssdt/wwi-ssdt";
        var root = CorpusManifest.Current.PathOf(folder);
        var project = XDocument.Load(Path.Combine(root, "WideWorldImporters.sqlproj"));
        var items = project.Descendants()
            .Where(e => e.Attribute("Include") is not null && e.Attribute("Include")!.Value.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(e => e.Attribute("Include")!.Value.Replace('\\', '/'), e => e.Name.LocalName, StringComparer.Ordinal);
        var built = items.Where(i => i.Value == "Build").Select(i => i.Key).Order(StringComparer.Ordinal).ToList();
        var others = Directory.EnumerateFiles(root, "*.sql", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(path => !built.Contains(path, StringComparer.Ordinal))
            .Select(path => (Path: path, Item: items.GetValueOrDefault(path, "none")))
            .ToList();

        Omission Group(Func<(string Path, string Item), bool> member, string why) =>
            new([.. others.Where(member).Select(o => o.Path).Order(StringComparer.Ordinal)], why);

        var leftOut = new List<Omission>
        {
            Group(
                o => o.Item == "PostDeploy",
                "The post-deployment script. SSDT runs it after deploying the schema, in sqlcmd mode, and it includes the scripts " +
                "below with `:r`, which haruspex doesn't follow."),
            Group(
                o => o.Item == "None" && o.Path.StartsWith("DataLoadSimulation/", StringComparison.Ordinal),
                "Procedures that the post-deployment script creates with `:r` once the schema is deployed, because the project's " +
                "build refuses them (the script's own comment says why). They are in the deployed database, so these procedures " +
                "are missing from the figures until haruspex follows `:r`."),
            Group(
                o => o.Item == "None" && o.Path.StartsWith("PostDeploymentScripts/", StringComparison.Ordinal),
                "Starter data that the post-deployment script loads with `INSERT`, `UPDATE`, `DELETE` and `EXEC`. Data, not schema: " +
                "they define no object, so reading them would add nothing."),
        };

        return new Corpus(
            "WideWorldImporters",
            folder,
            built,
            leftOut,
            "An SSDT project, `WideWorldImporters.sqlproj`, and its scripts. Reading a project file isn't built yet, so the " +
            "test reads the scripts the project lists to build, as a build would, and leaves out the others, " +
            "listed below with the reason. The project builds most of its scripts with `QUOTED_IDENTIFIER` off, and they are " +
            "parsed with it on; in these scripts every double quote is inside a string or a comment, where the setting " +
            "changes nothing.");
    }

    /// <summary>Scripts of the project left out of the run, and why.</summary>
    /// <param name="Files">The scripts, relative to the project's folder, in ordinal order.</param>
    /// <param name="Why">The reason, for the figures page.</param>
    internal sealed record Omission(IReadOnlyList<string> Files, string Why);
}
