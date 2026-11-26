using Haruspex.Collections;
using Haruspex.Ir;

namespace Haruspex.Cli;

/// <summary>What <c>haruspex generate</c> or <c>haruspex check</c> was asked, as the command line gave it: one module or several.</summary>
/// <param name="Sources">The folder of <c>.sql</c> files, the project file or the DACPAC, or several folders, as given; empty to take each config file's <c>source</c>.</param>
/// <param name="Configs">The config files <c>--config</c> names, as given, in order; empty to look for one, or with <c>--all</c>.</param>
/// <param name="All">The folder <c>--all</c> names, as given; empty when it names none (the working directory); null without <c>--all</c>.</param>
/// <param name="Include">The <c>--include</c> patterns, as given.</param>
/// <param name="Out">The folder <c>--out</c> names, as given, or null to take each config file's <c>out</c>.</param>
/// <param name="WorkingDirectory">Where relative paths start, and a config file is looked for.</param>
internal sealed record ModulesRequest(
    EquatableArray<string> Sources,
    EquatableArray<string> Configs,
    string? All,
    EquatableArray<string> Include,
    string? Out,
    string WorkingDirectory)
{
    /// <summary>The baseline <c>--baseline</c> names, as given, or null to take the config file's <c>baseline</c>, if any; for one module only.</summary>
    public string? Baseline { get; init; }

    /// <summary>The build of haruspex that runs, which each module's request carries (<see cref="RunRequest.Build"/>).</summary>
    public required IrBuild Build { get; init; }

    /// <summary>Whether <c>--fit-only</c> asks <c>check</c> for the fit with the baseline alone, without the output folder.</summary>
    public bool FitOnly { get; init; }

    /// <summary>A path given on the command line, made full against the working directory.</summary>
    public string Resolve(string path) => Path.GetFullPath(Path.Combine(WorkingDirectory, path));

    /// <summary>Whether the run may cover several modules: <c>--all</c>, or more than one <c>--config</c>.</summary>
    public bool Several => All is not null || Configs.Count > 1;
}
