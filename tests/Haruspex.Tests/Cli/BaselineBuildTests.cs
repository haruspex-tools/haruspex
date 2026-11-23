using Haruspex.Cli;
using Haruspex.Ir;

namespace Haruspex.Tests.Cli;

/// <summary>
/// The build that wrote an IR file, as the command line meets it (decision 0117): every IR file <c>generate</c> writes names the
/// build that runs; a baseline given as an IR file holds the claims of the build that wrote it, which the run warns of when that is
/// not the build that runs (HAR6043), steering to the source the file was inferred from; and neither <c>check</c> nor
/// <c>generate</c>'s account of what changed takes a file written by another build for a change.
/// </summary>
public class BaselineBuildTests
{
    /// <summary>A build that ran earlier: the same version as <see cref="FixedBuild"/>, from another commit.</summary>
    private static readonly Terminal Earlier = new() { Version = "0.0.0-test+1111111111111111111111111111111111111111" };

    /// <summary>A build made without its commit, as one from a <c>git archive</c> copy or a source tarball is.</summary>
    private static readonly Terminal Unknown = new() { Version = "0.0.0-preview" };

    private const string Tables = "CREATE TABLE shop.orders (order_id int NOT NULL PRIMARY KEY, customer nvarchar(50) NOT NULL);";

    private const string ListOrders = "CREATE PROCEDURE shop.list_orders AS\nSELECT o.order_id, o.customer FROM shop.orders AS o;";

    private const string Config = """{ "source": "db", "out": "generated", "schemas": { "shop": { "include": ["*"] } } }""";

    /// <summary>The project, its release kept in <c>release/db</c> and its IR, written by <paramref name="by"/>, in <c>release/haruspex.ir.json</c>.</summary>
    private static TempDirectory Released(Terminal by)
    {
        var folder = new TempDirectory();
        folder.File("haruspex.json", Config);
        folder.File("db/tables.sql", Tables);
        folder.File("db/procedures.sql", ListOrders);
        folder.File("release/db/tables.sql", Tables);
        folder.File("release/db/procedures.sql", ListOrders);
        Assert.Equal(0, Run(folder, by, "generate").ExitCode);
        folder.File("release/haruspex.ir.json", Written(folder));
        return folder;
    }

    private static (int ExitCode, string Output, string Error) Run(TempDirectory folder, Terminal terminal, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = HaruspexCli.Run(args, output, error, folder.Path, terminal);
        return (exitCode, CliOutput.Shown(output.ToString(), folder.Path), CliOutput.Shown(error.ToString(), folder.Path));
    }

    private static string Written(TempDirectory folder) => File.ReadAllText(Path.Combine(folder.Path, "generated", HaruspexCli.IrFileName));

    [Fact]
    public void Every_ir_file_generate_writes_names_the_build_that_runs_and_is_0_30()
    {
        using var folder = Released(FixedBuild.Terminal);

        var document = IrJsonReader.Read(Written(folder));

        Assert.Equal(IrDocument.CurrentVersion, document.Version);
        Assert.Equal(new IrBuild("0.0.0-test") { Commit = new string('0', 40) }, document.Source.WrittenBy);
        Assert.StartsWith("{\n  \"irVersion\": \"0.30\",\n  \"source\": {\n    \"dialect\": \"tsql\",\n    \"writtenBy\": {\n      \"version\": \"0.0.0-test\",\n      \"commit\": \"0000000000000000000000000000000000000000\"\n    },\n", Written(folder), StringComparison.Ordinal);
    }

    [Fact]
    public void A_build_without_its_commit_names_its_version_alone()
    {
        using var folder = Released(Unknown);

        Assert.Equal(new IrBuild("0.0.0-preview"), IrJsonReader.Read(Written(folder)).Source.WrittenBy);
        Assert.DoesNotContain("\"commit\"", Written(folder), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("generate")]
    [InlineData("check")]
    public void A_baseline_ir_written_by_another_build_is_warned_of_naming_both_and_steering_to_the_source(string command)
    {
        using var folder = Released(Earlier);
        Assert.Equal(0, Run(folder, FixedBuild.Terminal, "generate", "--baseline", "release/haruspex.ir.json").ExitCode);

        var (exitCode, _, error) = Run(folder, FixedBuild.Terminal, command, "--baseline", "release/haruspex.ir.json");

        Assert.Equal(0, exitCode);
        Assert.Contains(
            "warning HAR6043: the baseline release/haruspex.ir.json holds the claims of the haruspex that wrote it, 0.0.0-test (commit 111111111111), and this is 0.0.0-test (commit 000000000000). A fix made between the two may have withdrawn a claim the file holds, and the types would still rest on it. Give the baseline as the source it was inferred from (a .dacpac, a .sqlproj or a folder), which this haruspex infers again with its own rules.",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_baseline_ir_that_names_no_writer_is_warned_of_as_a_build_not_known()
    {
        using var folder = Released(FixedBuild.Terminal);
        var document = IrJsonReader.Read(File.ReadAllText(Path.Combine(folder.Path, "release", "haruspex.ir.json")));
        folder.File("release/haruspex.ir.json", IrJsonWriter.Write(document with { Source = document.Source with { WrittenBy = null } }));

        var (exitCode, _, error) = Run(folder, FixedBuild.Terminal, "generate", "--baseline", "release/haruspex.ir.json");

        Assert.Equal(0, exitCode);
        Assert.Contains("warning HAR6043: the baseline release/haruspex.ir.json is IR 0.25, which doesn't name the haruspex that wrote it, so its claims are those of a build not known; this is 0.0.0-test (commit 000000000000).", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_baseline_ir_this_build_wrote_is_taken_without_a_word()
    {
        using var folder = Released(FixedBuild.Terminal);

        var (exitCode, _, error) = Run(folder, FixedBuild.Terminal, "generate", "--baseline", "release/haruspex.ir.json");

        Assert.Equal(0, exitCode);
        Assert.Equal("", error);
    }

    [Fact]
    public void A_build_without_its_commit_can_t_tell_a_baseline_ir_is_its_own()
    {
        using var folder = Released(Unknown);

        var (exitCode, _, error) = Run(folder, Unknown, "generate", "--baseline", "release/haruspex.ir.json");

        Assert.Equal(0, exitCode);
        Assert.Contains("warning HAR6043: the baseline release/haruspex.ir.json holds the claims of the haruspex that wrote it, 0.0.0-preview (commit not known), and this is 0.0.0-preview (commit not known).", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_baseline_given_as_its_source_is_inferred_by_the_build_that_runs_and_not_warned_of()
    {
        using var folder = Released(Earlier);

        var (exitCode, _, error) = Run(folder, FixedBuild.Terminal, "generate", "--baseline", "release/db");

        Assert.Equal(0, exitCode);
        Assert.Equal("", error);
        Assert.Equal(FixedBuild.Terminal.Build, IrJsonReader.Read(Written(folder)).Source.Baseline!.WrittenBy);
    }

    [Fact]
    public void The_combined_ir_names_the_build_that_wrote_its_baseline_s_claims()
    {
        using var folder = Released(Earlier);

        Assert.Equal(0, Run(folder, FixedBuild.Terminal, "generate", "--baseline", "release/haruspex.ir.json").ExitCode);

        var source = IrJsonReader.Read(Written(folder)).Source;
        Assert.Equal(FixedBuild.Terminal.Build, source.WrittenBy);
        Assert.Equal(Earlier.Build, source.Baseline!.WrittenBy);
    }

    [Fact]
    public void Check_passes_an_ir_another_build_wrote_whose_claims_are_the_same()
    {
        using var folder = Released(Earlier);

        var (exitCode, output, error) = Run(folder, FixedBuild.Terminal, "check");

        Assert.Equal(0, exitCode);
        Assert.Equal("", error);
        Assert.Equal("haruspex: 1 procedure, no unknowns. generated is up to date.\n", output);
    }

    [Fact]
    public void Generate_says_no_claim_changed_when_only_the_build_did_and_writes_its_own()
    {
        using var folder = Released(Earlier);

        var (exitCode, output, _) = Run(folder, FixedBuild.Terminal, "generate");

        Assert.Equal(0, exitCode);
        Assert.Contains("\nNo claim changed since the last run, but the IR's record of where they came from did: a location, the build of haruspex that wrote it, or the output of another version.\n", output, StringComparison.Ordinal);
        Assert.Equal(FixedBuild.Terminal.Build, IrJsonReader.Read(Written(folder)).Source.WrittenBy);
    }

    [Theory]
    [InlineData("0.0.0-preview-0123abcd+0123abcd00000000000000000000000000000000", "0.0.0-preview-0123abcd", "0123abcd00000000000000000000000000000000")]
    [InlineData("1.2.3+0123abcd000000000000000000000000000000000000000000000000000000ff", "1.2.3", "0123abcd000000000000000000000000000000000000000000000000000000ff")]
    [InlineData("0.0.0-preview", "0.0.0-preview", null)]
    [InlineData("1.2.3+build.7", "1.2.3+build.7", null)]
    [InlineData("1.2.3+0123ABCD00000000000000000000000000000000", "1.2.3+0123ABCD00000000000000000000000000000000", null)]
    public void A_build_is_named_by_the_version_the_tool_says_split_at_its_commit(string says, string version, string? commit) =>
        Assert.Equal(new IrBuild(version) { Commit = commit }, Terminal.BuildOf(says));
}
