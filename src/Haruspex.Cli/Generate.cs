using Haruspex.Collections;
using Haruspex.Config;
using Haruspex.Diagnostics;
using Haruspex.Emit.Zod;
using Haruspex.Ir;
using Haruspex.SqlServer.Inference;
using Haruspex.SqlServer.Model;

namespace Haruspex.Cli;

/// <summary>
/// <c>haruspex generate</c>, as named steps: read the config file, settle what the run is asked, read the project, select
/// procedures, infer and apply the config, emit, and write. Each step returns an outcome, and they are composed with
/// <see cref="Outcome{T}.Then{TNext}"/>, so a step runs only on the value of the one before, and a failure carries everything
/// found so far to the end, where it is reported (docs/decisions/0037-steps-return-outcomes.md).
/// </summary>
internal static class Generate
{
    /// <summary>Runs the command, writing diagnostics to <paramref name="error"/> and the summary to <paramref name="output"/>.</summary>
    /// <returns>The exit code (<see cref="HaruspexCli.ExitCodes"/>).</returns>
    public static int Run(GenerateRequest request, TextWriter output, TextWriter error)
    {
        var run = ReadConfig(request)
            .Then(config => Settle(request, config))
            .Then(ReadProject)
            .Then(Select)
            .Then(Infer)
            .Then(Emit)
            .Then(Write)
            .Then(Noted);
        return Report(run, output, error);
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

    /// <summary>The procedures to type, and those the file's own selection names (for its overrides), when they differ.</summary>
    private sealed class Selection(Loaded loaded, EquatableArray<ObjectName> procedures, IReadOnlyCollection<ObjectName>? byFile)
    {
        public Loaded Loaded { get; } = loaded;

        public EquatableArray<ObjectName> Procedures { get; } = procedures;

        public IReadOnlyCollection<ObjectName>? ByFile { get; } = byFile;
    }

    /// <summary>The document with the config applied, and the Zod module emitted from it.</summary>
    private sealed class Output(Plan plan, IrDocument document, string zod)
    {
        public Plan Plan { get; } = plan;

        public IrDocument Document { get; } = document;

        public string Zod { get; } = zod;
    }

    /// <summary>The written output: where each file went, and the folder its SQL files are under.</summary>
    private sealed record Written(IrDocument Document, string IrPath, string ZodPath, string Source);

    /// <summary>The config file: named, or found by walking up from the working directory, or none.</summary>
    private static Outcome<Settings> ReadConfig(GenerateRequest request)
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
    /// The folders to read and write, and the command line's patterns, from the command line over the config file. The three
    /// don't depend on each other, so they are combined, and a problem with each is reported, not only the first. Whether
    /// anything is selected depends on the patterns, so it is checked only once they are read: an error there would
    /// otherwise cause another, about a selection that was never made.
    /// </summary>
    private static Outcome<Plan> Settle(GenerateRequest request, Settings settings) =>
        Outcome.Combine(
                Outcome.Combine(Source(request, settings), Out(request, settings)),
                Includes(request).Then(include => Selects(settings, include).Select(_ => include)))
            .Select(settled => new Plan(settings, settled.First.First, settled.First.Second, settled.Second));

    private static Outcome<string> Source(GenerateRequest request, Settings settings)
    {
        if ((request.Directory is { } given ? request.Resolve(given) : settings.Config.Source) is not { } directory)
        {
            return Refused<string>(CliCodes.NoSource, settings.Path is null
                ? "give the folder of .sql files to read, or a config file with \"source\"."
                : $"give the folder of .sql files to read, or \"source\" in {settings.Path}.");
        }

        return Directory.Exists(directory)
            ? new Succeeded<string>(directory)
            : Refused<string>(CliCodes.SourceNotFound, $"directory not found: {request.Directory ?? directory}");
    }

    private static Outcome<string> Out(GenerateRequest request, Settings settings) =>
        (request.Out is { } given ? request.Resolve(given) : settings.Config.Out) is { } output
            ? new Succeeded<string>(output)
            : Refused<string>(CliCodes.NoOut, settings.Path is null
                ? "give --out, the folder to write to, or a config file with \"out\"."
                : $"give --out, the folder to write to, or \"out\" in {settings.Path}.");

    /// <summary>The <c>--include</c> patterns, which replace the file's schema includes for one run; each schema's exclude still applies.</summary>
    private static Outcome<EquatableArray<ObjectPattern>> Includes(GenerateRequest request) => Outcome.All(request.Include.Select(Include));

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
                : $"nothing is selected: give --include, or a schema with \"include\" in {settings.Path}, such as \"schemas\": {{ \"dbo\": {{ \"include\": [\"*\"] }} }}.");

    /// <summary>The project, read from the source folder; its diagnostics, with their files under that folder.</summary>
    private static Outcome<Loaded> ReadProject(Plan plan)
    {
        var project = SqlProject.Load(plan.Source);
        return Outcome.Of(new Loaded(plan, project), project.Diagnostics.Select(d => Rooted(d, plan.Source)));
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
    private static Outcome<(Plan Plan, IrDocument Document)> Infer(Selection selection)
    {
        var plan = selection.Loaded.Plan;
        var applied = selection.Loaded.Project.Infer(selection.Procedures, plan.Config, selection.ByFile, plan.Out);
        return Rebased(applied, plan.Source).Select(document => (plan, document));
    }

    /// <summary>
    /// The Zod module. The emitter knows no config file; what it refuses is a name or an import the file's settings made, so
    /// it is reported against the file.
    /// </summary>
    private static Outcome<Output> Emit((Plan Plan, IrDocument Document) inferred)
    {
        var (plan, document) = inferred;
        var emitted = ZodEmitter.Emit(document, new ZodOptions
        {
            Objects = plan.Config.Objects == ObjectsPolicy.Strict ? ZodObjects.Strict : ZodObjects.Strip,
        });
        return Located(emitted, plan.Config.Location).Select(zod => new Output(plan, document, zod));
    }

    /// <summary>Both files, written to the output folder.</summary>
    private static Outcome<Written> Write(Output output)
    {
        var irPath = Path.Join(output.Plan.Out, HaruspexCli.IrFileName);
        var zodPath = Path.Join(output.Plan.Out, HaruspexCli.ZodFileName);
        try
        {
            Directory.CreateDirectory(output.Plan.Out);

            // File.WriteAllText writes UTF-8 without a byte order mark.
            File.WriteAllText(irPath, IrJsonWriter.Write(output.Document));
            File.WriteAllText(zodPath, output.Zod);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Refused<Written>(CliCodes.CannotWrite, $"cannot write to {output.Plan.Out}: {e.Message}");
        }

        return new Succeeded<Written>(new Written(output.Document, irPath, zodPath, output.Plan.Source));
    }

    /// <summary>
    /// Each unknown in what was written, as a note with its reason's code, where it is and why, so that none is silent. One
    /// that only a declaration can resolve is reported by the config layer instead, with what to declare, and is not said
    /// twice (<see cref="InferenceCodes.Of"/>).
    /// </summary>
    private static Succeeded<Written> Noted(Written written) =>
        new(written, [.. written.Document.Unknowns.Select(u => InferenceCodes.Of(u.Reason) is { } code
                ? Rooted(code.At(SourceLocation.TryParse(u.At, out var at) ? at : null, $"{u.Object} {u.Path}: {u.Detail}"), written.Source)
                : null)
            .OfType<Diagnostic>()]);

    /// <summary>Writes what the run found, and its summary, and gives the exit code.</summary>
    private static int Report(Outcome<Written> run, TextWriter output, TextWriter error)
    {
        foreach (var diagnostic in run.Diagnostics)
            error.WriteLine(diagnostic);

        return run switch
        {
            Succeeded<Written> { Value: var written } => Succeeded(written, output),
            Failed<Written> failed => Failed(failed, error),
        };
    }

    private static int Succeeded(Written written, TextWriter output)
    {
        var procedures = written.Document.Objects.Count;
        output.WriteLine(
            $"haruspex: {procedures} {(procedures == 1 ? "procedure" : "procedures")}, {written.Document.Unknowns.Count} unknown(s). Wrote {written.IrPath} and {written.ZodPath}.");
        return HaruspexCli.ExitCodes.Success;
    }

    private static int Failed(Failed<Written> failed, TextWriter error)
    {
        var sql = failed.Diagnostics.Count(d => d.IsError && d.At is SourceLocation);
        if (sql > 0)
            error.WriteLine($"haruspex: {sql} error(s); nothing was written.");
        return ExitCode(failed.Diagnostics);
    }

    /// <summary>
    /// The exit code of a failed run, by what failed (the contract in the CLI's README): the input itself, SQL that doesn't
    /// read, or output that can't be written, is 1; the command line or the config file, 2.
    /// </summary>
    private static int ExitCode(EquatableArray<Diagnostic> found) =>
        found.Any(d => d.IsError && (d.At is SourceLocation || d.Code == CliCodes.CannotWrite))
            ? HaruspexCli.ExitCodes.Failed
            : HaruspexCli.ExitCodes.Usage;

    /// <summary>A failure of the command line's own (<see cref="CliCodes"/>), with no place.</summary>
    private static Failed<T> Refused<T>(DiagnosticCode code, string message) => new([code.At(null, message)]);

    /// <summary>The outcome with each diagnostic's SQL file under <paramref name="source"/>, as the user names it.</summary>
    private static Outcome<T> Rebased<T>(Outcome<T> outcome, string source) => outcome switch
    {
        Succeeded<T> succeeded => new Succeeded<T>(succeeded.Value, [.. succeeded.Diagnostics.Select(d => Rooted(d, source))]),
        Failed<T> failed => new Failed<T>([.. failed.Diagnostics.Select(d => Rooted(d, source))]),
    };

    /// <summary>The outcome with each diagnostic that has no place placed at <paramref name="location"/>.</summary>
    private static Outcome<T> Located<T>(Outcome<T> outcome, DiagnosticLocation? location) => outcome switch
    {
        Succeeded<T> succeeded => new Succeeded<T>(succeeded.Value, [.. succeeded.Diagnostics.Select(d => d with { At = d.At ?? location })]),
        Failed<T> failed => new Failed<T>([.. failed.Diagnostics.Select(d => d with { At = d.At ?? location })]),
    };

    private static Diagnostic Rooted(Diagnostic diagnostic, string source) =>
        diagnostic.At is SourceLocation at ? diagnostic with { At = at with { File = Path.Join(source, at.File) } } : diagnostic;
}
