using Haruspex.Collections;
using Haruspex.Config;
using Haruspex.Diagnostics;

namespace Haruspex.Cli;

/// <summary>
/// The modules one run covers, and the run of a command over each: the config files <c>--config</c> names, every
/// <c>haruspex.json</c> below a folder (<c>--all</c>), or the one config file found by walking up, as before. Each module is
/// self-contained (its own selection, names, output and committed contract), so each runs as a run of its own would, and is
/// reported under its config file's path; the run fails if any module does, with the worst of their exit codes
/// (<see cref="RunReport.ExitCode"/>). One module runs exactly as a single run always has.
/// </summary>
internal static class ModuleSet
{
    /// <summary>Folders <c>--all</c> doesn't look in: another package's files, and hidden folders such as <c>.git</c>.</summary>
    private const string Packages = "node_modules";

    /// <summary>What marks a folder as the top of a repository or a worktree of its own: <c>.git</c>, a folder or a file.</summary>
    private const string Repository = ".git";

    /// <summary>Whether this platform's file systems usually ignore case, so that two output folders differing only in case are one.</summary>
    private static readonly bool CaseIgnored = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    /// <summary>
    /// What <c>--all</c> passes over: hidden and system folders, links to folders (which could lead back above), and the attributes
    /// the platform marks them with. Folders whose name starts with a dot, and <c>node_modules</c>, are left out by name.
    /// </summary>
    private static readonly EnumerationOptions Folders = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
    };

    /// <summary>Runs <paramref name="command"/> over each module the request covers, in turn, and gives what each found.</summary>
    public static RunReport Run(CommandName name, ModulesRequest request, Func<RunRequest, ModuleReport> command) =>
        Requests(request) switch
        {
            Failed<EquatableArray<RunRequest>> failed => new RunReport(name, [], request.Several, failed.Diagnostics),
            Succeeded<EquatableArray<RunRequest>> { Value: var requests } => new RunReport(name, [.. requests.Select(command)], request.Several, []),
        };

    /// <summary>
    /// A request for each module, or why there are none: options that apply to one module beside several, or <c>--all</c> with
    /// nothing below its folder to find. A config file named with <c>--config</c> is passed on as given, so a message about it
    /// names it so; with neither option, the one module's config file is looked for by walking up, as a single run does.
    /// </summary>
    private static Outcome<EquatableArray<RunRequest>> Requests(ModulesRequest request)
    {
        if (request.All is not null && request.Configs.Count > 0)
            return ModuleRun.Refused<EquatableArray<RunRequest>>(CliCodes.OptionsApart, "--all finds the config files itself: give --all or --config, not both.");
        if (request.Several && (request.Directory is not null || request.Include.Count > 0 || request.Out is not null || request.Baseline is not null))
        {
            return ModuleRun.Refused<EquatableArray<RunRequest>>(CliCodes.OptionsApart,
                "a source, --include, --out and --baseline are for one module; with several config files, each says its own source, selection, out and baseline.");
        }

        var configs = request.All is { } all
            ? Below(request, all)
            : new Succeeded<EquatableArray<string?>>(request.Configs.Count > 0 ? [.. request.Configs] : [null]);
        return configs
            .Select(found => (EquatableArray<RunRequest>)[.. found.Select(config =>
                new RunRequest(request.Directory, config, request.Include, request.Out, request.WorkingDirectory) { Baseline = request.Baseline, FitOnly = request.FitOnly })])
            .Then(Apart);
    }

    /// <summary>
    /// The modules, when no two write to one folder; otherwise each pair that would, refused before any runs: the second would
    /// overwrite the first's module, and a check would compare both with one pair of files. A module whose folder can't be
    /// settled is left to its own run to report.
    /// </summary>
    private static Outcome<EquatableArray<RunRequest>> Apart(EquatableArray<RunRequest> requests)
    {
        var first = new Dictionary<string, RunRequest>(CaseIgnored ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var shared = new List<Diagnostic>();
        foreach (var module in requests)
        {
            if (ModuleRun.OutOf(module) is not { } folder)
                continue;
            if (first.TryGetValue(folder, out var earlier))
            {
                shared.Add(CliCodes.SharedOut.At(null,
                    $"{Name(earlier)} and {Name(module)} both write to {DiagnosticFormatter.PathAsShown(folder)}, so one module would overwrite the other's. Give each module its own \"out\"."));
            }
            else
            {
                first.Add(folder, module);
            }
        }

        return shared.Count == 0 ? new Succeeded<EquatableArray<RunRequest>>(requests) : new Failed<EquatableArray<RunRequest>>([.. shared]);
    }

    /// <summary>A module's config file, from the working directory, as a message names it.</summary>
    private static string Name(RunRequest module) =>
        // Several modules always have their config files: --all found them, or --config named them.
        DiagnosticFormatter.PathAsShown(Path.GetRelativePath(module.WorkingDirectory, module.Resolve(module.Config ?? HaruspexConfig.FileName)));

    /// <summary>Every config file below the folder <c>--all</c> names (<paramref name="all"/>, as given), or why there is none.</summary>
    private static Outcome<EquatableArray<string?>> Below(ModulesRequest request, string all)
    {
        var shown = all.Length == 0 ? "the working directory" : all;
        var folder = request.Resolve(all.Length == 0 ? "." : all);
        if (!Directory.Exists(folder))
            return ModuleRun.Refused<EquatableArray<string?>>(CliCodes.NoConfigFound, $"--all: directory not found: {shown}");

        var found = Find(folder).ToList();
        return found.Count == 0
            ? ModuleRun.Refused<EquatableArray<string?>>(CliCodes.NoConfigFound, $"--all: no {HaruspexConfig.FileName} below {shown}. Give the folder that holds the modules' config files.")
            : new Succeeded<EquatableArray<string?>>([.. found]);
    }

    /// <summary>
    /// The config file in <paramref name="folder"/>, if any, then those below it, a folder at a time in ordinal order of names, so
    /// that the modules run, and are reported, in the same order on every machine. It doesn't go into a folder below that is the
    /// top of a repository or worktree of its own.
    /// </summary>
    private static IEnumerable<string> Find(string folder)
    {
        var config = Path.Join(folder, HaruspexConfig.FileName);
        if (File.Exists(config))
            yield return config;

        foreach (var below in Directory.EnumerateDirectories(folder, "*", Folders).Order(StringComparer.Ordinal))
        {
            // A repository or worktree nested below, such as a submodule or a worktree kept inside the checkout, is another
            // checkout's modules, at another commit: they are its own to check.
            var name = Path.GetFileName(below);
            if (name.StartsWith('.') || name == Packages || Path.Exists(Path.Join(below, Repository)))
                continue;
            foreach (var found in Find(below))
                yield return found;
        }
    }
}
