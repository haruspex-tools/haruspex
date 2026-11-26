using Haruspex.Collections;
using Haruspex.Config;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Inference;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Sources;
using Haruspex.SqlServer.Sources.Dacpacs;
using Haruspex.SqlServer.Sources.Projects;

namespace Haruspex.Cli;

/// <summary>
/// A database project read from a folder of <c>.sql</c> files, a project file (<c>.sqlproj</c>) or a DACPAC, ready to infer: the layers composed for the
/// command line (reading in Haruspex.SqlServer.Sources, inference in Haruspex.SqlServer.Inference, the config file's
/// selection, overrides and naming in Haruspex.Config).
/// </summary>
/// <remarks>
/// It is the composition, here in the CLI, because composing the layers is the CLI's job and no layer below may know
/// both how a model is read and how it is inferred.
/// </remarks>
internal sealed class SqlProject
{
    private readonly DatabaseModel model;

    /// <summary>What the IR's source record says of the packages read, or nothing for a folder (<see cref="Recorded(IEnumerable{ReferenceRead})"/>).</summary>
    private readonly Func<IrSource, IrSource> recorded;

    /// <summary>The folder a location's file is relative to (<see cref="FolderOf"/>).</summary>
    private readonly Func<string, string> folderOf;

    private SqlProject(DatabaseModel model, IReadOnlyList<Diagnostic> diagnostics, Func<string, string> folderOf, Func<IrSource, IrSource>? recorded = null)
    {
        this.model = model;
        this.folderOf = folderOf;
        this.recorded = recorded ?? (source => source);
        Diagnostics = diagnostics;
        // In name order, as a listing is: the model's own order is where each was read, which would make the order the
        // procedures are inferred in, and so of the warnings they give, depend on the files' names.
        Procedures = [.. model.Procedures.Select(p => p.Name).OrderBy(n => n.ToString(), StringComparer.Ordinal)];
    }

    /// <summary>The application's query files, read against the project and inferred with its procedures; none until <see cref="WithQueries"/>.</summary>
    public IReadOnlyList<QueryFile> Queries { get; private init; } = [];

    /// <summary>
    /// The project with the application's query files, whose locations are relative to <paramref name="directory"/>, the config
    /// file's folder, rather than to the project's.
    /// </summary>
    /// <param name="directory">The folder the files' paths are relative to.</param>
    /// <param name="queries">The files, read.</param>
    public SqlProject WithQueries(string directory, IReadOnlyList<QueryFile> queries)
    {
        var paths = queries.Select(q => q.Path).ToHashSet(StringComparer.Ordinal);
        return new SqlProject(model, Diagnostics, file => paths.Contains(file) ? directory : folderOf(file), recorded) { Queries = queries };
    }

    /// <summary>Errors found while reading the project. Results inferred despite them are not trustworthy.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    /// <summary>Every procedure in the project, in a stable order.</summary>
    public IReadOnlyList<ObjectName> Procedures { get; }

    /// <summary>
    /// Reads every <c>*.sql</c> file under <paramref name="directory"/>, whose database's default collation is
    /// <paramref name="collation"/> (the config file's), since a folder doesn't say; null when not known.
    /// </summary>
    /// <param name="directory">The folder.</param>
    /// <param name="collation">The config file's <c>collation</c>.</param>
    /// <param name="descriptions">The config file's <c>descriptions</c>: the extended properties read as descriptions.</param>
    public static SqlProject Load(string directory, string? collation = null, IReadOnlyCollection<string>? descriptions = null)
    {
        var read = SqlScriptFolder.Read(directory);
        var (model, diagnostics) = Described(read.Model.WithDeclaredCollation(collation), read.Diagnostics, descriptions);
        return new SqlProject(model, diagnostics, _ => directory);
    }

    /// <summary>
    /// Reads every <c>*.sql</c> file under each of <paramref name="folders"/> as one database, its locations relative to
    /// <paramref name="directory"/>, the config file's folder, so that each says which folder its script is in (decision 0127).
    /// </summary>
    /// <param name="directory">The config file's folder.</param>
    /// <param name="folders">The folders, relative to <paramref name="directory"/>.</param>
    /// <param name="collation">The config file's <c>collation</c>, since a folder doesn't say.</param>
    /// <param name="named">Each folder as the user wrote it, for messages; by default as <paramref name="folders"/> gives it.</param>
    /// <param name="descriptions">The config file's <c>descriptions</c>: the extended properties read as descriptions.</param>
    public static SqlProject LoadFolders(string directory, IReadOnlyList<string> folders, string? collation = null, IReadOnlyList<string>? named = null, IReadOnlyCollection<string>? descriptions = null)
    {
        var read = SqlScriptFolder.ReadFolders(directory, folders, named);
        var (model, diagnostics) = Described(read.Model.WithDeclaredCollation(collation), read.Diagnostics, descriptions);

        // The folders as found on disk, as the locations name them, so that a change of case alone in the list changes nothing.
        return new SqlProject(model, diagnostics, _ => directory, source => source with { Paths = read.Folders });
    }

    /// <summary>
    /// The model with its descriptions found under the config file's names (<c>MS_Description</c> unless it gives others), and
    /// the reading's diagnostics with a note for each passed over (docs/decisions/0128-a-column-s-description-reaches-the-fields-that-are-that-column.md).
    /// </summary>
    private static (DatabaseModel Model, IReadOnlyList<Diagnostic> Diagnostics) Described(DatabaseModel model, IReadOnlyList<Diagnostic> read, IReadOnlyCollection<string>? names)
    {
        var (described, notes) = DescriptionFinder.Find(model, names ?? DescriptionFinder.DefaultNames);
        return (described, [.. read, .. notes]);
    }

    /// <summary>The database's default collation, as the source records it or else as the config file declares it; null when neither does.</summary>
    public string? DefaultCollation => model.DefaultCollation;

    /// <summary>Whether <paramref name="source"/> names a DACPAC rather than a folder of scripts: a file, by its extension.</summary>
    public static bool IsPackage(string source) => source.EndsWith(".dacpac", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether <paramref name="source"/> names a database project's file rather than a folder of scripts: by its extension.</summary>
    public static bool IsProjectFile(string source) => source.EndsWith(".sqlproj", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads the project file at <paramref name="project"/> and what it refers to, as its build would, looking for a referenced
    /// DACPAC where the build takes it from and then in <paramref name="references"/>; the IR it infers records the packages read, as
    /// a DACPAC's does, and no package of its own (a project's scripts aren't hashed).
    /// </summary>
    /// <param name="project">The <c>.sqlproj</c>.</param>
    /// <param name="references">The config file's <c>references</c> folders.</param>
    /// <param name="collation">The config file's <c>collation</c>, used only when the project's isn't read (it always records one otherwise).</param>
    /// <param name="builds">The config file's <c>builds</c>: referenced packages declared built from a project.</param>
    /// <param name="descriptions">The config file's <c>descriptions</c>: the extended properties read as descriptions.</param>
    public static SqlProject LoadProject(string project, IReadOnlyList<string> references, string? collation = null, IReadOnlyCollection<string>? builds = null, IReadOnlyCollection<string>? descriptions = null) =>
        LoadProject(project, SqlProjectReader.Read(project, references, builds), collation, descriptions);

    /// <summary>The project at <paramref name="project"/>, from what reading it found.</summary>
    /// <param name="project">The <c>.sqlproj</c>.</param>
    /// <param name="read">What reading it found.</param>
    /// <param name="collation">The config file's <c>collation</c>.</param>
    /// <param name="descriptions">The config file's <c>descriptions</c>: the extended properties read as descriptions.</param>
    internal static SqlProject LoadProject(string project, SqlProjectReadResult read, string? collation = null, IReadOnlyCollection<string>? descriptions = null)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(project)) ?? ".";
        var packages = Folders(read.References.Select(r => r.Found).OfType<LoadedPackage>());
        var (model, diagnostics) = Described(read.Model.WithDeclaredCollation(collation), read.Diagnostics, descriptions);
        return new SqlProject(
            model,
            diagnostics,
            file => packages.GetValueOrDefault(file.Split('/')[0], folder),
            source => source with { References = Recorded(read.References), Builds = Builds(null, read.References, read.ReadAsBuilds) });
    }

    /// <summary>
    /// Reads the DACPAC at <paramref name="package"/> and the packages of its database it refers to, looking for them beside it
    /// and then in <paramref name="references"/>; the IR it infers records the packages read.
    /// </summary>
    /// <param name="package">The package.</param>
    /// <param name="references">The config file's <c>references</c> folders.</param>
    /// <param name="collation">The config file's <c>collation</c>, used only when the package records none.</param>
    /// <param name="builds">The config file's <c>builds</c>: packages declared built from a project, whose key constraints hold.</param>
    /// <param name="descriptions">The config file's <c>descriptions</c>: the extended properties read as descriptions.</param>
    public static SqlProject LoadPackage(string package, IReadOnlyList<string> references, string? collation = null, IReadOnlyCollection<string>? builds = null, IReadOnlyCollection<string>? descriptions = null)
    {
        var read = DacpacReader.Read(package, references, builds);
        var folders = Folders(read.References.Select(r => r.Found).Prepend(read.Package).OfType<LoadedPackage>());
        var beside = Path.GetDirectoryName(Path.GetFullPath(package)) ?? ".";
        var (model, diagnostics) = Described(read.Model.WithDeclaredCollation(collation), read.Diagnostics, descriptions);
        return new SqlProject(model, diagnostics, file => folders.GetValueOrDefault(file.Split('/')[0], beside), source => Recorded(source, read));
    }

    /// <summary>
    /// The folder a location's file is relative to, so that a path the user is shown exists: the source folder, or for a
    /// location in a DACPAC (<c>sales.dacpac/[sales].[get_order]</c>) the folder the package named first in it was found in,
    /// which for a referenced package may be a folder of the config file's <c>references</c>.
    /// </summary>
    /// <param name="file">The location's file.</param>
    public string FolderOf(string file) => folderOf(file);

    /// <summary>
    /// Where a null reason points, as a place in the SQL: the reason's own place (<c>path:line:column</c>), or, for a column
    /// declared nullable (<c>schema.table.column</c>), where the project declares that column. Null for anything else, such as
    /// a column of a table the project doesn't define.
    /// </summary>
    /// <param name="at">The reason's <c>at</c>.</param>
    public SourceLocation? PlaceOf(string at)
    {
        ArgumentNullException.ThrowIfNull(at);
        if (SourceLocation.TryParse(at, out var place))
            return place;
        return model.Tables
            .SelectMany(t => t.Columns.Select(c => (Name: $"{t.Name}.{c.Name}", c.At)))
            .FirstOrDefault(c => c.Name.Equals(at, StringComparison.OrdinalIgnoreCase)).At;
    }

    /// <summary>
    /// The IR's source record with the package and its references, by file name and SHA-256: the IR knows nothing of DACPACs,
    /// so the composition, which knows both, says what was read.
    /// </summary>
    private static IrSource Recorded(IrSource source, DacpacReadResult read) => source with
    {
        Package = read.Package is { } package ? new IrPackage(package.Name, package.Sha256) : null,
        References = Recorded(read.References),
        Builds = Builds(read.Package, read.References, read.ReadAsBuilds),
    };

    /// <summary>
    /// The packages read as builds because the config file declared them so, by the names the IR's source record gives them: the
    /// package's own name, or the name of each reference it was found for (decision 0122).
    /// </summary>
    /// <param name="package">The package the source was read from, or null for a project.</param>
    /// <param name="references">The packages it refers to.</param>
    /// <param name="readAsBuilds">The packages read as builds.</param>
    private static EquatableArray<string> Builds(LoadedPackage? package, IEnumerable<ReferenceRead> references, IReadOnlyCollection<LoadedPackage> readAsBuilds)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var build in readAsBuilds)
        {
            if (package is not null && package.Path == build.Path)
                names.Add(package.Name);
            foreach (var reference in references.Where(r => r.Found?.Path == build.Path))
                names.Add(reference.Name);
        }

        return [.. names];
    }

    /// <summary>The packages a source refers to, by file name and SHA-256, as the IR records them.</summary>
    private static EquatableArray<IrPackageReference> Recorded(IEnumerable<ReferenceRead> references) =>
        [.. references.Select(r => new IrPackageReference(r.Name, r.Found?.Sha256, r.Database))];

    /// <summary>The folder each package was found in, by its file name, the first for a name winning.</summary>
    private static Dictionary<string, string> Folders(IEnumerable<LoadedPackage> packages)
    {
        var folders = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var found in packages)
            folders.TryAdd(found.Name, Path.GetDirectoryName(found.Path) ?? ".");
        return folders;
    }

    /// <summary>Infers the IR for the given procedures and the query files, and applies the config file's overrides and naming to it.</summary>
    /// <param name="selected">The procedures to infer.</param>
    /// <param name="config">The config.</param>
    /// <param name="selectedByFile">See <see cref="ConfigApplication.Apply"/>.</param>
    /// <param name="outputDirectory">See <see cref="ConfigApplication.Apply"/>.</param>
    /// <returns>
    /// The document with the config applied, after what inference could not check (such as a write to a table the project
    /// does not define, whose triggers it cannot see), which doesn't stop generation; or every problem.
    /// </returns>
    /// <remarks>
    /// The config file's declared results (<c>procedures["s.p"].results</c>) are handed to inference, in the IR's terms, so that
    /// a call of a declared procedure contributes them to its caller; a procedure's own declaration is applied by the config
    /// layer, which checks it against what is inferred. The declared procedures of the project that are not selected are
    /// inferred too, for that check alone.
    /// </remarks>
    public Outcome<IrDocument> Infer(IReadOnlyList<ObjectName> selected, HaruspexConfig config, IReadOnlyCollection<ObjectName>? selectedByFile = null, string? outputDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(config);
        var declarations = new Dictionary<ObjectName, DeclaredResults>();
        foreach (var entry in config.Procedures)
        {
            if (entry is DeclaredResultsOverride { Results: var declared, Procedure: var name })
                declarations.TryAdd(name, new DeclaredResults(declared.Results, declared.IrTypes, declared.Why));
        }

        // A column declared to hold JSON is part of the column's type, so inference reads it wherever the column is read; a
        // declaration the project can't hold stops the run, before anything is inferred from it.
        var columnProblems = ColumnDeclarationCheck.Check(config.Columns, model, (view, column) => IrInference.ViewColumnSources(model, view, column));
        if (columnProblems.Count > 0)
            return new Failed<IrDocument>([.. columnProblems]);
        List<DeclaredColumn> columns = [.. config.Columns.Select(c => new DeclaredColumn(c.Table, c.Column, c.Content, c.Types, c.Record))];

        var inferred = IrInference.Infer(model, selected, declarations, config.Session, Queries, columns).Select(d => d with { Source = recorded(d.Source) });
        var others = declarations.Keys
            .Select(name => model.FindProcedure(name)?.Name)
            .OfType<ObjectName>()
            .Where(name => !selected.Contains(name))
            .Distinct()
            .ToList();

        // Inferred only to check their declarations against: what inference couldn't check of them is not this run's to report.
        var declaredDocument = others.Count == 0 ? null : IrInference.Infer(model, others, declarations, config.Session, columns: columns).Value;
        // Each script the locations point into is recorded with its hash, for explain to match a file against before it shows a line.
        return inferred.Then(document => ConfigApplication.Apply(document, config, Procedures, selectedByFile, outputDirectory, declaredDocument, Queries))
            .Select(document => document with { Source = document.Source with { Files = ScriptFiles.Of(document, folderOf) } });
    }
}
