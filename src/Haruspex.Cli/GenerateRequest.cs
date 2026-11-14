using Haruspex.Collections;

namespace Haruspex.Cli;

/// <summary>What <c>haruspex generate</c> was asked, as the command line gave it.</summary>
/// <param name="Directory">The folder of <c>.sql</c> files or the DACPAC, as given, or null to take the config file's <c>source</c>.</param>
/// <param name="Config">The config file <c>--config</c> names, as given, or null to look for one.</param>
/// <param name="Include">The <c>--include</c> patterns, as given.</param>
/// <param name="Out">The folder <c>--out</c> names, as given, or null to take the config file's <c>out</c>.</param>
/// <param name="WorkingDirectory">Where relative paths start, and the config file is looked for.</param>
internal sealed record GenerateRequest(string? Directory, string? Config, EquatableArray<string> Include, string? Out, string WorkingDirectory)
{
    /// <summary>A path given on the command line, made full against the working directory.</summary>
    public string Resolve(string path) => Path.GetFullPath(Path.Combine(WorkingDirectory, path));
}
