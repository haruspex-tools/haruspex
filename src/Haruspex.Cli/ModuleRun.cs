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
            .Then(plan => Said(progress, $"reading {DisplayPath.From(request.WorkingDirectory, plan.Source)}", plan).Then(ReadProject))
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
            .Select(plan => new Plan(new Settings(plan.Config with { AppliedToBaseline = true }, plan.Settings.Path), plan.Source, plan.Out, plan.Include))
            .Then(plan => Said(progress, $"reading {DisplayPath.From(request.WorkingDirectory, plan.Source)}", plan).Then(ReadProject))
            .Then(Select)
            .Then(selection => Said(progress, Inferring(selection), selection).Then(Infer))
            .Select(inferred => inferred.Database);

    /// <summary>The value, once <paramref name="what"/> has been said as the run's step.</summary>
    private static Succeeded<T> Said<T>(Progress progress, string what, T value)
    {
        progress.Step(what);
        return new Succeeded<T>(value);
    }

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
    /// What the run is asked: the settings, the folders to read and write, and the command line's patterns. Like each value
    /// handed from one step to the next, a class: nothing compares two.
    /// </summary>
    private sealed class Plan(Settings settings, string source, string @out, EquatableArray<ObjectPattern> include)
    {
        public Settings Settings { get; } = settings;

        public string Source { get; } = source;

        public string Out { get; } = @out;

        public EquatableArray<ObjectPattern> Include { get; } = include;

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
            .Select(settled => new Plan(settings, settled.First.Source, settled.First.Out, settled.Second)));
    }

    /// <summary>The folders to read and write, which don't depend on each other, so a problem with each is reported.</summary>
    private static Outcome<(string Source, string Out)> Folders(RunRequest request, Settings settings) =>
        Outcome.Combine(Source(request, settings), Out(request, settings));

    /// <summary>The folder of scripts, the project file or the DACPAC to read, which must exist.</summary>
    private static Outcome<string> Source(RunRequest request, Settings settings)
    {
        if ((request.Directory is { } given ? request.Resolve(given) : settings.Config.Source) is not { } source)
        {
            return Refused<string>(CliCodes.NoSource, settings.Path is null
                ? "give the folder of .sql files, the .sqlproj or the .dacpac to read, or a config file with \"source\"."
                : $"give the folder of .sql files, the .sqlproj or the .dacpac to read, or \"source\" in {DiagnosticFormatter.PathAsShown(settings.Path)}.");
        }

        if (SqlProject.IsPackage(source) || SqlProject.IsProjectFile(source))
        {
            return File.Exists(source)
                ? new Succeeded<string>(source)
                : Refused<string>(CliCodes.SourceNotFound, $"file not found: {request.Directory ?? DiagnosticFormatter.PathAsShown(source)}");
        }

        return Directory.Exists(source)
            ? new Succeeded<string>(source)
            : Refused<string>(CliCodes.SourceNotFound, $"directory not found: {request.Directory ?? DiagnosticFormatter.PathAsShown(source)}");
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
        var project = package ? SqlProject.LoadPackage(plan.Source, plan.Config.References, collation)
            : projectFile ? SqlProject.LoadProject(plan.Source, plan.Config.References, collation)
            : SqlProject.Load(plan.Source, collation);
        IEnumerable<Diagnostic> unused = package || projectFile || plan.Config.References.Count == 0
            ? []
            : [CliCodes.ReferencesUnused.At(plan.Config.Location, $"\"references\" is read only when the source is a .dacpac or a .sqlproj, and {plan.Source} is a folder of scripts.")];
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
        var applied = project.Infer(selection.Procedures, plan.Config, selection.ByFile, plan.Out);
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
        var strict = plan.Config.Objects == ObjectsPolicy.Strict;
        var files = plan.Config.Emitters.Select(emitter => emitter switch
        {
            Emitter.Zod => ZodEmitter.Emit(document, new ZodOptions { Objects = strict ? ZodObjects.Strict : ZodObjects.Strip })
                .Select(text => new EmittedFile(HaruspexCli.ZodFileName, text)),
            Emitter.JsonSchema => JsonSchemaEmitter.Emit(document, new JsonSchemaOptions { Objects = strict ? JsonSchemaObjects.Strict : JsonSchemaObjects.Strip })
                .Select(text => new EmittedFile(HaruspexCli.JsonSchemaFileName, text)),

            // TypeScript's reserved words are the Zod emitter's knowledge, handed to this one as they are to the config's naming.
            // "objects": "strict" refuses a result set the functions don't know, as it refuses a key the schemas don't.
            Emitter.Mssql => MssqlEmitter.Emit(
                    document,
                    new MssqlOptions { SchemaModule = HaruspexCli.ZodModuleSpecifier, ServerOnly = plan.Config.ServerOnly, IsReserved = TypeScriptNames.IsReserved, RefusesExtraResultSets = strict })
                .Select(text => new EmittedFile(HaruspexCli.MssqlFileName, text)),
            Emitter.Mapping => new Succeeded<EmittedFile>(new EmittedFile(HaruspexCli.MappingFileName, MappingModule.Text)),
        });
        return Located(Outcome.All(files), plan.Config.Location).Select(emitted => new EmittedModule(plan.Config, plan.Out, project, document, emitted)
        {
            Baseline = typed.Baseline,
            Database = typed.Database,
            Combination = typed.Combination,
        });
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
        found.Any(d => d.IsError && (d.At is SourceLocation || d.Code == SourceCodes.UnreadablePackage || d.Code == CliCodes.CannotWrite || CliCodes.CheckFailures.Contains(d.Code)))
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
