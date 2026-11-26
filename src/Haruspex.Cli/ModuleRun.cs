using Haruspex.Cli.Output;
using Haruspex.Collections;
using Haruspex.Config;
using Haruspex.Diagnostics;
using Haruspex.Emit.JsonSchema;
using Haruspex.Emit.Mssql;
using Haruspex.Emit.Zod;
using Haruspex.Ir;
using Haruspex.SqlServer.Inference;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Sources;

namespace Haruspex.Cli;

/// <summary>
/// The steps <c>haruspex generate</c> and <c>haruspex check</c> share, up to the module in memory: read the config file, settle
/// what the run is asked, read the project, select procedures, infer and apply the config, and emit. Each step returns an
/// outcome, and they are composed with <see cref="Outcome{T}.Then{TNext}"/>, so a step runs only on the value of the one
/// before, and a failure carries everything found so far to the end, where it is reported
/// (docs/decisions/0037-steps-return-outcomes.md). <see cref="Generate"/> writes what they give, and <see cref="Check"/>
/// compares it with the files in the output folder; the guide's chapter docs/internals/12-from-input-to-output.md follows a run through them.
/// </summary>
internal static class ModuleRun
{
    /// <summary>The module the request would write, in memory, or why there is none.</summary>
    /// <param name="request">What the run is asked.</param>
    /// <param name="progress">Where to say which step the run is at, for a run long enough to show it.</param>
    public static Outcome<EmittedModule> Emitted(RunRequest request, Progress progress) =>
        Settle(request)
            .Then(plan => Said(progress, Reading(request, plan), plan).Then(ReadProject))
            .Then(Select)
            .Then(selection => Said(progress, Inferring(selection), selection).Then(Infer))
            .Then(inferred => Combined(request, progress, inferred))
            .Then(Emit);

    /// <summary>
    /// The IR a baseline given as a source has, with the module's own config file applied as to a baseline
    /// (<see cref="HaruspexConfig.AppliedToBaseline"/>), without emitting it: so that the two are selected, overridden and named
    /// alike, and a procedure whose named sets the change adds to or takes from doesn't stop the run.
    /// </summary>
    public static Outcome<IrDocument> InferredAsBaseline(RunRequest request, Progress progress) =>
        Settle(request)
            .Select(plan => new Plan(new Settings(plan.Config with { AppliedToBaseline = true }, plan.Settings.Path), plan.Sources, plan.Out, plan.Include, plan.Build) { Named = plan.Named })
            .Then(plan => Said(progress, Reading(request, plan), plan).Then(ReadProject))
            .Then(Select)
            .Then(selection => Said(progress, Inferring(selection), selection).Then(Infer))
            .Select(inferred => inferred.Database);

    /// <summary>The value, once <paramref name="what"/> has been said as the run's step.</summary>
    private static Succeeded<T> Said<T>(Progress progress, string what, T value)
    {
        progress.Step(what);
        return new Succeeded<T>(value);
    }

    /// <summary>The step of reading, with what it reads: <c>reading database</c>, or <c>reading tables, procedures</c> for several folders.</summary>
    private static string Reading(RunRequest request, Plan plan) => $"reading {string.Join(", ", plan.Sources.Select(s => DisplayPath.From(request.WorkingDirectory, s)))}";

    /// <summary>The step of inference, with what it infers: <c>inferring 12 procedures and 3 query files</c>.</summary>
    private static string Inferring(Selection selection)
    {
        var queries = selection.Loaded.Project.Queries.Count;
        var procedures = TextReport.Counted(selection.Procedures.Count, "procedure");
        return queries == 0 ? $"inferring {procedures}" : $"inferring {procedures} and {TextReport.Counted(queries, "query file")}";
    }

    /// <summary>The config file and what it says; the defaults when there is none.</summary>
    private sealed record Settings(HaruspexConfig Config, string? Path);

    /// <summary>
    /// What the run is asked: the settings, the folders to read and write, the command line's patterns, and the build that runs.
    /// Like each value handed from one step to the next, a class: nothing compares two.
    /// </summary>
    private sealed class Plan(Settings settings, EquatableArray<string> sources, string @out, EquatableArray<ObjectPattern> include, IrBuild build)
    {
        public Settings Settings { get; } = settings;

        /// <summary>What to read, full paths: one folder, project file or package, or several folders read as one database.</summary>
        public EquatableArray<string> Sources { get; } = sources;

        /// <summary>Each source as the command line or the config file wrote it, for messages; empty where not known.</summary>
        public EquatableArray<string> Named { get; init; } = [];

        /// <summary>The one source, or the first of several folders.</summary>
        public string Source => Sources[0];

        /// <summary>Whether the source is several folders, read as one database (decision 0127).</summary>
        public bool Folders => Sources.Count > 1;

        /// <summary>
        /// Each folder of several, relative to the config file's folder and <c>/</c>-separated, as the IR records them, as messages name
        /// them, and as the folder reader is given them.
        /// </summary>
        public EquatableArray<string> Shown => [.. Sources.Select(s => Path.GetRelativePath(Config.Directory, s).Replace('\\', '/'))];

        public string Out { get; } = @out;

        public EquatableArray<ObjectPattern> Include { get; } = include;

        /// <summary>The build that runs, whose claims the documents it infers hold (<see cref="IrSource.WrittenBy"/>).</summary>
        public IrBuild Build { get; } = build;

        public HaruspexConfig Config => Settings.Config;

        /// <summary>Whether <c>--include</c> replaces the config file's selection for this run.</summary>
        public bool FromCommandLine => Include.Count > 0;
    }

    /// <summary>The plan and the project it read.</summary>
    private sealed class Loaded(Plan plan, SqlProject project)
    {
        public Plan Plan { get; } = plan;

        public SqlProject Project { get; } = project;
    }

    /// <summary>
    /// The procedures to type, and those the file's own selection names (for its overrides), when they differ; the project holds
    /// the query files read with them.
    /// </summary>
    private sealed class Selection(Loaded loaded, EquatableArray<ObjectName> procedures, IReadOnlyCollection<ObjectName>? byFile)
    {
        public Loaded Loaded { get; } = loaded;

        public EquatableArray<ObjectName> Procedures { get; } = procedures;

        public IReadOnlyCollection<ObjectName>? ByFile { get; } = byFile;
    }

    /// <summary>The config file: named, or found by walking up from the working directory, or none.</summary>
    private static Outcome<Settings> ReadConfig(RunRequest request)
    {
        // Names are settled for TypeScript, whose reserved words are the Zod emitter's to know; the config is told them.
        var reserved = new ReservedWords(TypeScriptNames.Language, TypeScriptNames.IsReserved);
        var path = request.Config is { } named ? request.Resolve(named) : ConfigReader.Find(request.WorkingDirectory);
        if (path is null)
            return new Succeeded<Settings>(new Settings(HaruspexConfig.None(request.WorkingDirectory, reserved), null));
        if (!File.Exists(path))
            return Refused<Settings>(CliCodes.ConfigNotFound, $"config file not found: {request.Config}");
        return ConfigReader.Read(path, reserved).Then<Settings>(config => Acknowledgements.Refused(config).ToList() is { Count: > 0 } refused
            ? new Failed<Settings>([.. refused])
            : new Succeeded<Settings>(new Settings(config, path)));
    }

    /// <summary>
    /// What the run is asked: the config file, the folders it names or the command line gives, and the command line's patterns.
    /// The patterns don't depend on the config file, so a problem with each is reported, not only the first: a config file
    /// that doesn't read and a malformed pattern are both said. The folders depend on the config file, and whether anything
    /// is selected on the config file and the patterns, so they follow it, combined with each other: a problem with each
    /// is said, but none about a setting or a selection that was never made.
    /// </summary>
    private static Outcome<Plan> Settle(RunRequest request)
    {
        var include = Includes(request);
        var read = ReadConfig(request);
        if (read is not Succeeded<Settings>)
            return new Failed<Plan>([.. read.Diagnostics, .. include.Diagnostics]);
        return read.Then(settings => Outcome.Combine(
                Folders(request, settings),
                include.Then(patterns => Selects(settings, patterns).Select(_ => patterns)))
            .Select(settled => new Plan(settings, settled.First.Sources.Full, settled.First.Out, settled.Second, request.Build) { Named = settled.First.Sources.Named }));
    }

    /// <summary>What to read, full paths, and each named as the command line or the config file wrote it, for messages.</summary>
    private sealed record SourcesRead(EquatableArray<string> Full, EquatableArray<string> Named);

    /// <summary>The folders to read and write, which don't depend on each other, so a problem with each is reported.</summary>
    private static Outcome<(SourcesRead Sources, string Out)> Folders(RunRequest request, Settings settings) =>
        Outcome.Combine(Sources(request, settings), Out(request, settings));

    /// <summary>
    /// What to read, which must exist: the folder of scripts, the project file or the DACPAC; or several folders of scripts, the
    /// command line's or the config file's list, none of them a project or a package, each problem said by the entry at fault.
    /// </summary>
    private static Outcome<SourcesRead> Sources(RunRequest request, Settings settings)
    {
        // Each source as given on the command line, else as the config file names it, with its full path.
        (string Given, string Full)[] sources = request.Sources.Count > 0
            ? [.. request.Sources.Select(given => (given, request.Resolve(given)))]
            : settings.Config.SourceFolders.Count > 0 ? [.. settings.Config.SourceFolders.Select(written => (written, Path.GetFullPath(Path.Combine(settings.Config.Directory, written))))]
            : settings.Config.Source is { } one ? [(DiagnosticFormatter.PathAsShown(one), one)]
            : [];
        if (sources.Length == 0)
        {
            return Refused<SourcesRead>(CliCodes.NoSource, settings.Path is null
                ? "give the folder of .sql files, the .sqlproj or the .dacpac to read, or a config file with \"source\"."
                : $"give the folder of .sql files, the .sqlproj or the .dacpac to read, or \"source\" in {DiagnosticFormatter.PathAsShown(settings.Path)}.");
        }

        if (sources.Length > 1)
        {
            // Only folders make a list: a project or a package is a database of its own. The config file's list is checked as it is read.
            var whole = sources.Where(s => SqlProject.IsPackage(s.Full) || SqlProject.IsProjectFile(s.Full)).ToList();
            if (whole.Count > 0)
            {
                return new Failed<SourcesRead>([.. whole.Select(s => CliCodes.SourcesNotFolders.At(null,
                    $"{s.Given} is a {(SqlProject.IsPackage(s.Full) ? ".dacpac" : ".sqlproj")}, a whole database, so it can't be read with other sources: give it alone, or give only folders of .sql files."))]);
            }
        }

        var missing = sources.Where(s => !(SqlProject.IsPackage(s.Full) || SqlProject.IsProjectFile(s.Full) ? File.Exists(s.Full) : Directory.Exists(s.Full))).ToList();
        if (missing.Count > 0)
        {
            return new Failed<SourcesRead>([.. missing.Select(s => CliCodes.SourceNotFound.At(null,
                $"{(SqlProject.IsPackage(s.Full) || SqlProject.IsProjectFile(s.Full) ? "file" : "directory")} not found: {s.Given}"))]);
        }

        return new Succeeded<SourcesRead>(new SourcesRead([.. sources.Select(s => s.Full)], [.. sources.Select(s => s.Given)]));
    }

    private static Outcome<string> Out(RunRequest request, Settings settings) =>
        (request.Out is { } given ? request.Resolve(given) : settings.Config.Out) is { } output
            ? new Succeeded<string>(output)
            : Refused<string>(CliCodes.NoOut, settings.Path is null
                ? "give --out, the folder to write to, or a config file with \"out\"."
                : $"give --out, the folder to write to, or \"out\" in {DiagnosticFormatter.PathAsShown(settings.Path)}.");

    /// <summary>The <c>--include</c> patterns, which replace the file's schema includes for one run; each schema's exclude still applies.</summary>
    private static Outcome<EquatableArray<ObjectPattern>> Includes(RunRequest request) => Outcome.All(request.Include.Select(Include));

    private static Outcome<ObjectPattern> Include(string text) =>
        ObjectPattern.TryParse(text, out var pattern, out var problem)
            ? new Succeeded<ObjectPattern>(pattern)
            : Refused<ObjectPattern>(CliCodes.BadInclude, $"invalid --include pattern: {problem}");

    /// <summary>Nothing is selected without an include or query files: typing every procedure is asked for, never the default.</summary>
    private static Outcome<bool> Selects(Settings settings, EquatableArray<ObjectPattern> include) =>
        include.Count > 0 || settings.Config.SelectsAnything
            ? new Succeeded<bool>(true)
            : Refused<bool>(CliCodes.NothingSelected, settings.Path is null
                ? "nothing is selected: give --include, or a config file whose \"schemas\" include procedures or whose \"queries\" names query files. Use '*.*' to type every procedure."
                : $"nothing is selected: give --include, or a schema with \"include\" or \"queries\" in {DiagnosticFormatter.PathAsShown(settings.Path)}, such as \"schemas\": {{ \"dbo\": {{ \"include\": [\"*\"] }} }}.");

    /// <summary>
    /// The project, read from the source folder, project file or DACPAC; its diagnostics, with their files under the folder each was
    /// read from. The config file's <c>references</c> beside a folder of scripts is said, not ignored: a folder has no references to
    /// find.
    /// </summary>
    private static Outcome<Loaded> ReadProject(Plan plan)
    {
        var package = SqlProject.IsPackage(plan.Source);
        var projectFile = SqlProject.IsProjectFile(plan.Source);
        var collation = plan.Config.Collation;
        var project = plan.Folders ? SqlProject.LoadFolders(plan.Config.Directory, plan.Shown, collation, plan.Named.Count == plan.Sources.Count ? plan.Named : null)
            : package ? SqlProject.LoadPackage(plan.Source, plan.Config.References, collation, plan.Config.Builds)
            : projectFile ? SqlProject.LoadProject(plan.Source, plan.Config.References, collation, plan.Config.Builds)
            : SqlProject.Load(plan.Source, collation);
        IEnumerable<Diagnostic> unused = package || projectFile || plan.Config.References.Count == 0
            ? []
            : [CliCodes.ReferencesUnused.At(plan.Config.Location, plan.Folders
                ? $"\"references\" is read only when the source is a .dacpac or a .sqlproj, and {string.Join(", ", plan.Shown)} are folders of scripts."
                : $"\"references\" is read only when the source is a .dacpac or a .sqlproj, and {plan.Source} is a folder of scripts.")];
        IEnumerable<Diagnostic> overruled = collation is null || string.Equals(project.DefaultCollation, collation, StringComparison.OrdinalIgnoreCase)
            ? []
            : [CliCodes.CollationOverruled.At(plan.Config.Location, $"\"collation\" is {collation}, but {Path.GetFileName(plan.Source)} records {project.DefaultCollation}, the database's own, which is the one used.")];
        return Outcome.Of(new Loaded(plan, project), [.. unused, .. overruled, .. project.Diagnostics.Select(d => Rooted(d, project))]);
    }

    /// <summary>
    /// The procedures to type, and the query files. A pattern or a schema that matches nothing is an error, not silence: a typo
    /// would otherwise drop an interface from the generated code without anyone noticing. Each is reported, the query files'
    /// problems and the procedures' together.
    /// </summary>
    private static Outcome<Selection> Select(Loaded loaded) =>
        Outcome.Combine(SelectProcedures(loaded), Queries(loaded.Plan.Config))
            .Select(both => new Selection(new Loaded(loaded.Plan, loaded.Project.WithQueries(loaded.Plan.Config.Directory, both.Second)), both.First.Procedures, both.First.ByFile));

    /// <summary>
    /// The application's query files the config file names, each read; a file that doesn't read is reported where it is, under the
    /// config file's folder, and the run fails, as for a script of the project.
    /// </summary>
    private static Outcome<IReadOnlyList<QueryFile>> Queries(HaruspexConfig config)
    {
        var read = QuerySelection.Select(config)
            .Then(found => Outcome.All(found.Select(file => QueryFileReader.Read(file.Path, file.Relative))).Select(files => (IReadOnlyList<QueryFile>)files));
        Diagnostic Under(Diagnostic d) => d.At is SourceLocation at ? d with { At = at with { File = Path.Join(config.Directory, at.File) } } : d;
        return read switch
        {
            Succeeded<IReadOnlyList<QueryFile>> succeeded => new Succeeded<IReadOnlyList<QueryFile>>(succeeded.Value, [.. succeeded.Diagnostics.Select(Under)]),
            Failed<IReadOnlyList<QueryFile>> failed => new Failed<IReadOnlyList<QueryFile>>([.. failed.Diagnostics.Select(Under)]),
        };
    }

    /// <summary>The procedures to type.</summary>
    private static Outcome<Selection> SelectProcedures(Loaded loaded)
    {
        var plan = loaded.Plan;
        var project = loaded.Project;
        var selection = plan.FromCommandLine
            ? ProcedureSelection.Select(project.Procedures, plan.Include, plan.Config)
            : ProcedureSelection.Select(project.Procedures, plan.Config);

        // The file's own selection still decides which overrides are of selected procedures; problems with its patterns are
        // left for a run without --include, which this one-off isn't about.
        var byFile = plan.FromCommandLine ? ProcedureSelection.SelectedByFile(project.Procedures, plan.Config).ToHashSet() : null;
        return selection.Select(selected => new Selection(loaded, selected, byFile));
    }

    /// <summary>The IR of the selected procedures, with the config file's overrides and naming applied.</summary>
    private static Outcome<Typed> Infer(Selection selection)
    {
        var plan = selection.Loaded.Plan;
        var project = selection.Loaded.Project;
        // The source as the config file names it, relative to its folder and /-separated, so the IR says the same on every machine;
        // and the build that runs, whose claims these are (decision 0117).
        // Several folders are recorded each as found on disk, by the project, and no one path (decision 0127).
        var path = plan.Folders ? null : Path.GetRelativePath(plan.Config.Directory, plan.Source).Replace('\\', '/');
        var applied = project.Infer(selection.Procedures, plan.Config, selection.ByFile, plan.Out)
            .Select(document => document with { Source = document.Source with { Path = path, WrittenBy = plan.Build } });
        return Rebased(applied, project).Select(document => new Typed(plan, project, document, document));
    }

    /// <summary>
    /// What is emitted: the plan and project, the document the module is written from, and the database's own document; with a
    /// baseline, the baseline read and what combining the two found.
    /// </summary>
    private sealed class Typed(Plan plan, SqlProject project, IrDocument document, IrDocument database)
    {
        public Plan Plan { get; } = plan;

        public SqlProject Project { get; } = project;

        public IrDocument Document { get; } = document;

        public IrDocument Database { get; } = database;

        public Baseline? Baseline { get; init; }

        public EquatableArray<Diagnostic> Combination { get; init; } = [];
    }

    /// <summary>
    /// With a baseline, it read and the document combined with it, so that the module's types work against both databases
    /// (<see cref="CombinedTypes"/>); without one, the document as it is. <c>check --fit-only</c> asks only whether the database
    /// fits the baseline, so it reads the baseline but combines nothing.
    /// </summary>
    private static Outcome<Typed> Combined(RunRequest request, Progress progress, Typed typed)
    {
        var config = typed.Plan.Config;
        if (Baseline.Named(request, config) is not { } path)
            return new Succeeded<Typed>(typed);
        return Baseline.Read(request, config, path, progress).Then(baseline => request.FitOnly
            ? new Succeeded<Typed>(new Typed(typed.Plan, typed.Project, typed.Document, typed.Database) { Baseline = baseline })
            : CombinedTypes.Of(typed.Database, baseline, config, typed.Project, DisplayPath.From(request.WorkingDirectory, path))
                .Select(combined => new Typed(typed.Plan, typed.Project, combined.Document, typed.Database) { Baseline = baseline, Combination = combined.Found }));
    }

    /// <summary>
    /// What each emitter the config file names gives (<see cref="HaruspexConfig.Emitters"/>), in its order. An emitter knows no
    /// config file, so its settings are mapped across here; what it refuses is a name or an import the file's settings made, so it
    /// is reported against the file.
    /// </summary>
    private static Outcome<EmittedModule> Emit(Typed typed)
    {
        var (plan, project, document) = (typed.Plan, typed.Project, typed.Document);

        // A config file that splits its output writes a module's files per module, which SplitModules names.
        var files = SplitModules.Assigned(plan.Config, project, document).Then(assigned =>
        {
            if (plan.Config.Split is null)
                return OneModule(plan, document).Select(one => (Files: one, Modules: (EquatableArray<string>)[]));
            var modules = IrModules.Of(document, assigned);
            return SplitModules.Emit(plan.Config, document, modules).Select(split => (Files: split, Modules: modules.Names));
        });
        return Located(files, plan.Config.Location).Select(emitted => new EmittedModule(plan.Config, plan.Out, project, document, emitted.Files)
        {
            Modules = emitted.Modules,
            Baseline = typed.Baseline,
            Database = typed.Database,
            Combination = typed.Combination,
        });
    }

    /// <summary>Each emitter's one file, in the config file's order, as a config file that doesn't split its output writes them.</summary>
    private static Outcome<EquatableArray<EmittedFile>> OneModule(Plan plan, IrDocument document)
    {
        var strict = plan.Config.Objects == ObjectsPolicy.Strict;
        var files = plan.Config.Emitters.Select(emitter => emitter switch
        {
            Emitter.Zod => ZodEmitter.Emit(document, new ZodOptions { Objects = strict ? ZodObjects.Strict : ZodObjects.Strip, Comments = plan.Config.Comments })
                .Select(text => new EmittedFile(HaruspexCli.ZodFileName, text)),
            Emitter.JsonSchema => JsonSchemaEmitter.Emit(document, new JsonSchemaOptions { Objects = strict ? JsonSchemaObjects.Strict : JsonSchemaObjects.Strip, Comments = plan.Config.Comments })
                .Select(text => new EmittedFile(HaruspexCli.JsonSchemaFileName, text)),

            // TypeScript's reserved words are the Zod emitter's knowledge, handed to this one as they are to the config's naming.
            // "objects": "strict" refuses a result set the functions don't know, as it refuses a key the schemas don't.
            Emitter.Mssql => MssqlEmitter.Emit(
                    document,
                    new MssqlOptions { SchemaModule = HaruspexCli.ZodModuleSpecifier, ServerOnly = plan.Config.ServerOnly, IsReserved = TypeScriptNames.IsReserved, RefusesExtraResultSets = strict, Comments = plan.Config.Comments })
                .Select(text => new EmittedFile(HaruspexCli.MssqlFileName, text)),
            Emitter.Mapping => new Succeeded<EmittedFile>(new EmittedFile(HaruspexCli.MappingFileName, MappingModule.Text)),
        });
        return Outcome.All(files);
    }

    /// <summary>
    /// The output folder <paramref name="request"/> writes or checks, as the run would settle it (the command line's <c>--out</c>
    /// over the config file's <c>out</c>), or null when it can't be settled, which the run itself then reports.
    /// </summary>
    public static string? OutOf(RunRequest request) =>
        ReadConfig(request) is Succeeded<Settings> { Value: var settings } && Out(request, settings) is Succeeded<string> { Value: var folder }
            ? folder
            : null;

    /// <summary>
    /// The config file <paramref name="request"/> reads, as a run would find it, or null when there is none or it can't be read,
    /// which a run itself reports: what <c>explain</c> finds the IR and the SQL by.
    /// </summary>
    public static HaruspexConfig? ConfigOf(RunRequest request) =>
        ReadConfig(request) is Succeeded<Settings> { Value: { Path: not null } settings } ? settings.Config : null;

    /// <summary>
    /// Each unknown in <paramref name="document"/>, with the note it is reported by: its reason's code, where it is and why, so
    /// that none is silent. One that only a declaration can resolve is reported by the config layer instead, with what to declare,
    /// and is not said twice (<see cref="InferenceCodes.Of"/>), so it has no note.
    /// </summary>
    public static EquatableArray<NotedUnknown> Notes(IrDocument document, SqlProject project) =>
    [
        .. document.Unknowns.Select(u => new NotedUnknown(u, InferenceCodes.Of(u.Reason) is { } code
            ? Rooted(code.At(SourceLocation.TryParse(u.At, out var at) ? at : null, $"{u.Object} {u.Path}: {u.Detail}"), project)
            : null)),
    ];

    /// <summary>
    /// The exit code of a failed run, by what failed (the contract in the CLI's README): the input itself (SQL that doesn't read,
    /// a DACPAC that can't be), output that can't be written, or a check that finds the module changed or can't compare it, is
    /// 1; the command line or the config file, 2.
    /// </summary>
    public static int ExitCode(IEnumerable<Diagnostic> found) =>
        found.Any(d => d.IsError && (d.At is SourceLocation || d.Code == SourceCodes.UnreadablePackage || d.Code == SourceCodes.UnreadableScript || d.Code == CliCodes.CannotWrite || CliCodes.CheckFailures.Contains(d.Code)))
            ? HaruspexCli.ExitCodes.Failed
            : HaruspexCli.ExitCodes.Usage;

    /// <summary>A failure of the command line's own (<see cref="CliCodes"/>), with no place.</summary>
    public static Failed<T> Refused<T>(DiagnosticCode code, string message) => new([code.At(null, message)]);

    /// <summary>The outcome with each diagnostic's SQL file under the folder it was read from.</summary>
    private static Outcome<T> Rebased<T>(Outcome<T> outcome, SqlProject project) => outcome switch
    {
        Succeeded<T> succeeded => new Succeeded<T>(succeeded.Value, [.. succeeded.Diagnostics.Select(d => Rooted(d, project))]),
        Failed<T> failed => new Failed<T>([.. failed.Diagnostics.Select(d => Rooted(d, project))]),
    };

    /// <summary>The outcome with each diagnostic that has no place placed at <paramref name="location"/>.</summary>
    private static Outcome<T> Located<T>(Outcome<T> outcome, DiagnosticLocation? location) => outcome switch
    {
        Succeeded<T> succeeded => new Succeeded<T>(succeeded.Value, [.. succeeded.Diagnostics.Select(d => d with { At = d.At ?? location })]),
        Failed<T> failed => new Failed<T>([.. failed.Diagnostics.Select(d => d with { At = d.At ?? location })]),
    };

    /// <summary>The diagnostic with its SQL location's file under the folder it was read from, as the user names it (<see cref="SqlProject.FolderOf"/>).</summary>
    public static Diagnostic Rooted(Diagnostic diagnostic, SqlProject project) =>
        diagnostic.At is SourceLocation at ? diagnostic with { At = at with { File = Joined(project.FolderOf(at.File), at.File) } } : diagnostic;

    /// <summary>
    /// A location's file under its folder, as one path: a file of a project beside the one given (<c>../press/press.sqlproj</c>) is
    /// named where it is, not through the folder it is relative to.
    /// </summary>
    private static string Joined(string folder, string file) =>
        file.StartsWith("../", StringComparison.Ordinal) ? Path.GetFullPath(Path.Join(folder, file)) : Path.Join(folder, file);
}
