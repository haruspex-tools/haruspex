using Haruspex.Collections;
using Haruspex.Config;
using Haruspex.Diagnostics;
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
/// compares it with what is committed; the guide's chapter docs/internals/12-from-input-to-output.md follows a run through them.
/// </summary>
internal static class ModuleRun
{
    /// <summary>The module the request would write, in memory, or why there is none.</summary>
    public static Outcome<EmittedModule> Emitted(RunRequest request) =>
        Settle(request)
            .Then(ReadProject)
            .Then(Select)
            .Then(Infer)
            .Then(Emit);

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

    /// <summary>The procedures to type, and those the file's own selection names (for its overrides), when they differ.</summary>
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
        return ConfigReader.Read(path, reserved).Select(config => new Settings(config, path));
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
                : Refused<string>(CliCodes.SourceNotFound, $"file not found: {request.Directory ?? source}");
        }

        return Directory.Exists(source)
            ? new Succeeded<string>(source)
            : Refused<string>(CliCodes.SourceNotFound, $"directory not found: {request.Directory ?? source}");
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

    /// <summary>Nothing is selected without an include: typing every procedure is asked for, never the default.</summary>
    private static Outcome<bool> Selects(Settings settings, EquatableArray<ObjectPattern> include) =>
        include.Count > 0 || settings.Config.SelectsAnything
            ? new Succeeded<bool>(true)
            : Refused<bool>(CliCodes.NothingSelected, settings.Path is null
                ? "nothing is selected: give --include, or a config file whose \"schemas\" include procedures. Use '*.*' to type every procedure."
                : $"nothing is selected: give --include, or a schema with \"include\" in {DiagnosticFormatter.PathAsShown(settings.Path)}, such as \"schemas\": {{ \"dbo\": {{ \"include\": [\"*\"] }} }}.");

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
    /// The procedures to type. A pattern or a schema that matches nothing is an error, not silence: a typo would otherwise
    /// drop an interface from the generated code without anyone noticing.
    /// </summary>
    private static Outcome<Selection> Select(Loaded loaded)
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
    private static Outcome<(Plan Plan, SqlProject Project, IrDocument Document)> Infer(Selection selection)
    {
        var plan = selection.Loaded.Plan;
        var project = selection.Loaded.Project;
        var applied = project.Infer(selection.Procedures, plan.Config, selection.ByFile, plan.Out);
        return Rebased(applied, project).Select(document => (plan, project, document));
    }

    /// <summary>
    /// The Zod module. The emitter knows no config file; what it refuses is a name or an import the file's settings made, so
    /// it is reported against the file.
    /// </summary>
    private static Outcome<EmittedModule> Emit((Plan Plan, SqlProject Project, IrDocument Document) inferred)
    {
        var (plan, project, document) = inferred;
        var emitted = ZodEmitter.Emit(document, new ZodOptions
        {
            Objects = plan.Config.Objects == ObjectsPolicy.Strict ? ZodObjects.Strict : ZodObjects.Strip,
        });
        return Located(emitted, plan.Config.Location).Select(zod => new EmittedModule(plan.Config, plan.Out, project, document, zod));
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
    /// Each unknown in <paramref name="document"/>, as a note with its reason's code, where it is and why, so that none is
    /// silent. One that only a declaration can resolve is reported by the config layer instead, with what to declare, and is not
    /// said twice (<see cref="InferenceCodes.Of"/>).
    /// </summary>
    public static IEnumerable<Diagnostic> Notes(IrDocument document, SqlProject project) =>
        document.Unknowns.Select(u => InferenceCodes.Of(u.Reason) is { } code
                ? Rooted(code.At(SourceLocation.TryParse(u.At, out var at) ? at : null, $"{u.Object} {u.Path}: {u.Detail}"), project)
                : null)
            .OfType<Diagnostic>();

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
