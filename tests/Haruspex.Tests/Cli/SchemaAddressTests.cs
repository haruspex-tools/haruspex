using System.Runtime.InteropServices;
using System.Text.Json;
using Haruspex.Cli;

namespace Haruspex.Tests.Cli;

/// <summary>
/// Where the command names the schemas of its config file and its JSON report (<see cref="SchemaAddress"/>): a release build at its
/// own tag, <c>schemas/v&lt;version&gt;</c>, and every other build at <c>schemas/main</c>. A build is a release when it is built with
/// <c>-p:HaruspexRelease=true</c>, which drops the preview's suffix and marks the binary; one marked so without its own version is
/// refused by the build.
/// </summary>
public class SchemaAddressTests
{
    private static readonly Terminal Release = new() { Version = "1.2.3+0123abcd00000000000000000000000000000000", IsRelease = true };

    private static readonly Terminal Preview = new() { Version = "0.0.0-preview-0123abcd+0123abcd00000000000000000000000000000000", IsRelease = false };

    private static string Init(Terminal terminal)
    {
        using var folder = new TempDirectory();
        folder.File("database/p.sql", "CREATE PROCEDURE dbo.p AS SELECT 1 AS one;");
        Assert.Equal(0, HaruspexCli.Run(["init"], TextWriter.Null, TextWriter.Null, folder.Path, terminal));
        return File.ReadAllText(Path.Combine(folder.Path, "haruspex.json"));
    }

    private static string ReportSchema(Terminal terminal)
    {
        using var folder = new TempDirectory();
        folder.File("db/p.sql", "CREATE PROCEDURE dbo.p AS SELECT 1 AS one;");
        using var output = new StringWriter();
        HaruspexCli.Run(["generate", "db", "--include", "dbo.*", "--out", "out", "--format", "json"], output, TextWriter.Null, folder.Path, terminal);
        using var report = JsonDocument.Parse(output.ToString());
        return report.RootElement.GetProperty("$schema").GetString()!;
    }

    [Fact]
    public void A_release_s_init_names_the_config_schema_at_its_own_tag() =>
        Assert.Contains("  \"$schema\": \"https://haruspex.tools/schemas/v1.2.3/haruspex.schema.json\",\n", Init(Release), StringComparison.Ordinal);

    [Fact]
    public void Any_other_build_s_init_names_the_config_schema_at_main() =>
        Assert.Contains("  \"$schema\": \"https://haruspex.tools/schemas/main/haruspex.schema.json\",\n", Init(Preview), StringComparison.Ordinal);

    [Fact]
    public void A_release_s_json_report_names_its_schema_at_its_own_tag() =>
        Assert.Equal("https://haruspex.tools/schemas/v1.2.3/haruspex.output.schema.json", ReportSchema(Release));

    [Fact]
    public void Any_other_build_s_json_report_names_its_schema_at_main() =>
        Assert.Equal("https://haruspex.tools/schemas/main/haruspex.output.schema.json", ReportSchema(Preview));

    [Fact]
    public void The_build_the_tests_run_is_not_a_release() => Assert.False(Terminal.ToolIsRelease);

    [Fact]
    public void The_release_property_marks_the_binary_and_drops_the_preview_s_suffix()
    {
        var release = Evaluated("-p:HaruspexRelease=true", "-p:VersionPrefix=1.2.3");
        var preview = Evaluated();

        Assert.Equal(("1.2.3", true), (release.Version, release.Marked));
        Assert.Equal(("0.0.0-preview", false), (preview.Version, preview.Marked));
    }

    /// <summary>
    /// The command's project as MSBuild evaluates it with <paramref name="properties"/>: its version, and whether it marks the binary
    /// a release. The SDK is the one the tests run on, found beside its runtime.
    /// </summary>
    private static (string Version, bool Marked) Evaluated(params string[] properties)
    {
        var dotnet = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
        string[] args = ["msbuild", Path.Combine("src", "Haruspex.Cli", "Haruspex.Cli.csproj"), "-getProperty:Version", "-getItem:AssemblyMetadata", .. properties];
        Assert.True(EmitCheckPackage.TryRun(dotnet, args, RepositoryPaths.Root, out var output, out var why), $"MSBuild didn't evaluate the project ({why}):\n{output}");
        using var evaluated = JsonDocument.Parse(output);
        var marked = evaluated.RootElement.GetProperty("Items").GetProperty("AssemblyMetadata").EnumerateArray()
            .Any(m => m.GetProperty("Identity").GetString() == "HaruspexRelease" && m.GetProperty("Value").GetString() == "true");
        return (evaluated.RootElement.GetProperty("Properties").GetProperty("Version").GetString()!, marked);
    }
}
