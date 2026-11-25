using Haruspex.Ir;

namespace Haruspex.Cli;

/// <summary>
/// The address at which the command names a schema of its own that has no version of its own: the config file's, which
/// <c>haruspex init</c> writes into <c>$schema</c>, and the JSON report's, which each report names. A release build names the
/// schema at its own tag (<c>https://haruspex.tools/schemas/v1.2.3/haruspex.schema.json</c>), so that a file it wrote is checked
/// against what that release reads, whatever <c>main</c> holds later; every other build, a preview's included, names it at
/// <c>main</c> (<c>schemas/main/</c>), which no tag pins and which moves with the code. The site serves each tag's schemas as that
/// tag holds them (<see cref="HaruspexSite.Schemas"/>). The IR's schemas name their own version in their file names instead
/// (<c>IrJsonSchema.Id</c>), so they need no tag. A release is a build marked so (<see cref="Terminal.IsRelease"/>), as
/// packaging/npm/README.md says, from the commit tagged <c>v</c> and its version.
/// </summary>
internal static class SchemaAddress
{
    /// <summary>The config file's schema: <c>schemas/haruspex.schema.json</c>.</summary>
    public const string ConfigFile = "haruspex.schema.json";

    /// <summary>The JSON report's schema: <c>schemas/haruspex.output.schema.json</c>.</summary>
    public const string Report = "haruspex.output.schema.json";

    /// <summary>The address of the schema <paramref name="file"/> (in the repository's <c>schemas</c> folder) for the build that runs in <paramref name="terminal"/>.</summary>
    public static string Of(string file, Terminal terminal)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        var at = terminal.IsRelease ? "v" + terminal.Build.Version : "main";
        return $"{HaruspexSite.Schemas}{at}/{file}";
    }
}
