using System.Text.RegularExpressions;
using Haruspex.Cli;

namespace Haruspex.Tests.Packaging;

/// <summary>
/// The version the command says (<c>--version</c>) is the version of the npm packages it ships in: a preview is
/// <c>0.0.0-preview-</c> and the commit's first eight hex digits, as <c>packaging/npm/version.mjs</c> names the packages, then
/// a <c>+</c> and the whole commit. Built outside a git checkout, it is <c>0.0.0-preview</c>, with no commit to name. The pack
/// script refuses a binary whose version string isn't the packages' (its own tests show it), so the two can't disagree in a
/// tarball.
/// </summary>
public partial class VersionTests
{
    [Fact]
    public void The_command_s_version_is_the_preview_s_named_by_its_commit()
    {
        var version = Terminal.ToolVersion;

        if (version != "0.0.0-preview")
            Assert.Matches(PreviewVersion(), version);
    }

    [Fact]
    public void The_version_option_prints_it()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.Equal(0, HaruspexCli.Run(["--version"], output, error, RepositoryPaths.Root));
        Assert.Equal(Terminal.ToolVersion, output.ToString().Trim());
    }

    /// <summary>The version with its commit: the first eight hex digits after <c>preview-</c> are the whole commit's first eight.</summary>
    [GeneratedRegex("^0\\.0\\.0-preview-(?<short>[0-9a-f]{8})\\+\\k<short>[0-9a-f]{32}$")]
    private static partial Regex PreviewVersion();
}
