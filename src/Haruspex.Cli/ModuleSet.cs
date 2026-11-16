using Haruspex.Collections;
using Haruspex.Config;
using Haruspex.Diagnostics;

namespace Haruspex.Cli;

/// <summary>
/// The modules one run covers, and the run of a command over each: the config files <c>--config</c> names, every
/// <c>haruspex.json</c> below a folder (<c>--all</c>), or the one config file found by walking up, as before. Each module is
/// self-contained (its own selection, names, output and committed contract), so each runs as a run of its own would, and is
/// reported under its config file's path; the run fails if any module does, with the worst of their exit codes. One module
/// runs exactly as a single run always has, with nothing added to what it prints.
/// </summary>
internal static class ModuleSet
{
    /// <summary>Folders <c>--all</c> doesn't look in: another package's files, and hidden folders such as <c>.git</c>.</summary>
    private const string Packages = "node_modules";

    /// <summary>
    /// What <c>--all</c> passes over: hidden and system folders, links to folders (which could lead back above), and the attributes
    /// the platform marks them with. Folders whose name starts with a dot, and <c>node_modules</c>, are left out by name.
    /// </summary>
    private static readonly EnumerationOptions Folders = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
    };

    /// <summary>Runs <paramref name="command"/> over each module the request covers, and gives the run's exit code.</summary>
    public static int Run(ModulesRequest request, Func<RunRequest, TextWriter, TextWriter, int> command, TextWriter output, TextWriter error) =>
        Requests(request) switch
        {
            Failed<EquatableArray<RunRequest>> failed => Refused(failed, error),
            Succeeded<EquatableArray<RunRequest>> { Value: var requests } when !request.Several => command(requests.Single(), output, error),
            Succeeded<EquatableArray<RunRequest>> { Value: var requests } => Each(requests, command, output, error),
        };

    private static int Refused(Failed<EquatableArray<RunRequest>> failed, TextWriter error)
    {
        foreach (var diagnostic in failed.Diagnostics)
            error.WriteLine(diagnostic);
        return HaruspexCli.ExitCodes.Usage;
    }

    /// <summary>Each module in turn, under a line naming its config file, then one line saying which failed.</summary>
    private static int Each(EquatableArray<RunRequest> requests, Func<RunRequest, TextWriter, TextWriter, int> command, TextWriter output, TextWriter error)
    {
        var failed = new List<string>();
        var worst = HaruspexCli.ExitCodes.Success;
        foreach (var module in requests)
        {
            // Several modules always have their config files: --all found them, or --config named them.
            var name = Path.GetRelativePath(module.WorkingDirectory, module.Resolve(module.Config ?? HaruspexConfig.FileName)).Replace('\\', '/');
            error.WriteLine($"haruspex: module {name}");
            var exitCode = command(module, output, error);
            if (exitCode != HaruspexCli.ExitCodes.Success)
                failed.Add(name);
            worst = Math.Max(worst, exitCode);
        }

        output.WriteLine(failed.Count == 0
            ? $"haruspex: {requests.Count} modules, none failed."
            : $"haruspex: {requests.Count} modules; {failed.Count} failed: {string.Join(", ", failed)}.");
        return worst;
    }

    /// <summary>
    /// A request for each module, or why there are none: options that apply to one module beside several, or <c>--all</c> with
    /// nothing below its folder to find. A config file named with <c>--config</c> is passed on as given, so a message about it
    /// names it so; with neither option, the one module's config file is looked for by walking up, as a single run does.
    /// </summary>
    private static Outcome<EquatableArray<RunRequest>> Requests(ModulesRequest request)
    {
        if (request.All is not null && request.Configs.Count > 0)
            return ModuleRun.Refused<EquatableArray<RunRequest>>(CliCodes.OptionsApart, "--all finds the config files itself: give --all or --config, not both.");
        if (request.Several && (request.Directory is not null || request.Include.Count > 0 || request.Out is not null))
        {
            return ModuleRun.Refused<EquatableArray<RunRequest>>(CliCodes.OptionsApart,
                "a source, --include and --out are for one module; with several config files, each says its own source, selection and out.");
        }

        var configs = request.All is { } all
            ? Below(request, all)
            : new Succeeded<EquatableArray<string?>>(request.Configs.Count > 0 ? [.. request.Configs] : [null]);
        return configs.Select(found => (EquatableArray<RunRequest>)[.. found.Select(config =>
            new RunRequest(request.Directory, config, request.Include, request.Out, request.WorkingDirectory))]);
    }

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
    /// that the modules run, and are reported, in the same order on every machine.
    /// </summary>
    private static IEnumerable<string> Find(string folder)
    {
        var config = Path.Join(folder, HaruspexConfig.FileName);
        if (File.Exists(config))
            yield return config;

        foreach (var below in Directory.EnumerateDirectories(folder, "*", Folders).Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(below);
            if (name.StartsWith('.') || name == Packages)
                continue;
            foreach (var found in Find(below))
                yield return found;
        }
    }
}
