using Haruspex.Cli.Output;
using Haruspex.Config;
using Haruspex.Diagnostics;
using Haruspex.Ir;

namespace Haruspex.Cli;

/// <summary>
/// The baseline a run was given (<c>--baseline</c>, or <c>"baseline"</c> in the config file), read: the database the application
/// meets before the change being made is deployed. <c>generate</c> combines it with the module's own database
/// (<see cref="CombinedTypes"/>), and <c>check</c> asks whether the module's database fits it (<see cref="BaselineCheck"/>). It is
/// read once, here, for both. Like each value handed from one step to the next, a class: nothing compares two.
/// </summary>
internal sealed class Baseline(string path, IrDocument document, bool isIr)
{
    /// <summary>The baseline, full, as the command line or the config file named it.</summary>
    public string Path { get; } = path;

    /// <summary>Its document: the IR file's, or the one inferred from the source with the module's config file.</summary>
    public IrDocument Document { get; } = document;

    /// <summary>Whether it was given as an IR file, which holds each query file's text as the application was built with it.</summary>
    public bool IsIr { get; } = isIr;

    /// <summary>The baseline the run names, full: the command line's over the config file's; null when neither names one.</summary>
    public static string? Named(RunRequest request, HaruspexConfig config) =>
        request.Baseline is { } given ? request.Resolve(given) : config.Baseline;

    /// <summary>
    /// The baseline at <paramref name="path"/>, read: an IR file as it is, of a version this haruspex reads; any other source
    /// inferred as the module's own is, with its config file and command line. Never fetched: a missing one is an error.
    /// </summary>
    public static Outcome<Baseline> Read(RunRequest request, HaruspexConfig config, string path, Progress progress)
    {
        var shown = DisplayPath.From(request.WorkingDirectory, path);
        if (!File.Exists(path) && !Directory.Exists(path))
            return new Failed<Baseline>([CliCodes.BaselineNotFound.At(request.Baseline is null ? config.Location : null, $"the baseline {shown} isn't there. haruspex never fetches it: put it there first (in CI, a step before this one), or correct --baseline or \"baseline\".")]);

        var isIr = File.Exists(path) && string.Equals(System.IO.Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase);
        progress.Step($"reading the baseline {shown}");
        // An IR file is the baseline's source as the config file names it, which the combined IR labels the baseline's side with.
        return (isIr ? ReadIr(path, shown).Select(d => d with { Source = d.Source with { Path = System.IO.Path.GetRelativePath(config.Directory, path).Replace('\\', '/') } }) : Inferred(request, path, progress))
            .Select(document => new Baseline(path, document, isIr));
    }

    /// <summary>An IR file as the baseline: of a version this haruspex reads, or refused, since a claim's meaning may differ between versions.</summary>
    private static Outcome<IrDocument> ReadIr(string path, string shown) => EarlierOutput.ReadIr(path) switch
    {
        EarlierIr.Readable { Document: var document } => new Succeeded<IrDocument>(document),
        EarlierIr.OtherVersion { Version: var version } => ModuleRun.Refused<IrDocument>(CliCodes.BaselineUnreadable,
            $"the baseline {shown} is IR {version}, and this haruspex reads IR {EnglishList.Of([.. IrDocument.ReadableVersions])}, so the two can't be compared claim by claim. Give the baseline as the source it was inferred from (a .dacpac, a .sqlproj or a folder), which this version reads."),
        EarlierIr.Unreadable { Problem: var problem } => ModuleRun.Refused<IrDocument>(CliCodes.BaselineUnreadable, $"the baseline {shown} can't be read as IR: {problem}"),

        // Absent is said before the file is read. The arm is a discard because the analyser that checks a switch over a closed
        // hierarchy covers each case doesn't look at this assembly (EarlierIr).
        _ => throw new InvalidOperationException($"No baseline at {path}."),
    };

    /// <summary>
    /// A source as the baseline, read and inferred with the module's own config file and command line, so that it selects the
    /// same procedures and applies the same overrides and names, and reads the query files as they are now. Only its errors are
    /// said: a warning is about the old source, which nobody is changing.
    /// </summary>
    private static Outcome<IrDocument> Inferred(RunRequest request, string baseline, Progress progress) =>
        ModuleRun.InferredAsBaseline(request with { Directory = baseline, Baseline = null }, progress) switch
        {
            Succeeded<IrDocument> { Value: var document } => new Succeeded<IrDocument>(document),
            var failed => new Failed<IrDocument>([.. failed.Diagnostics.Where(d => d.IsError)]),
        };
}
