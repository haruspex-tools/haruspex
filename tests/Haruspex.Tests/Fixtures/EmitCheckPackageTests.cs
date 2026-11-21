namespace Haruspex.Tests.Fixtures;

/// <summary>
/// How the tests start node: a command line longer than Windows allows is refused on every platform, so a list of files that grows
/// with the fixtures fails where it is written, not on Windows alone (<see cref="EmitCheckPackage.LongestCommandLine"/>).
/// </summary>
public class EmitCheckPackageTests
{
    [Fact]
    public void A_command_line_longer_than_windows_allows_is_refused_on_every_platform()
    {
        var files = Enumerable.Range(0, 200).Select(i => Path.Combine(Path.GetTempPath(), "haruspex-tests", $"fixture-{i:D3}.jsonschema.json")).ToArray();

        Assert.False(EmitCheckPackage.TryRun("node", ["check.mjs", .. files], RepositoryPaths.EmitCheck, out var output, out var why));
        Assert.Equal("", output);
        Assert.Contains("more than the 8,191 Windows allows", why, StringComparison.Ordinal);
    }

    // "node" "a" "bc": each part counted as quoted, a space before each argument; .NET quotes only a part that needs it, so this is
    // the most the line can be.
    [Fact]
    public void A_command_line_is_measured_as_if_every_part_were_quoted() =>
        Assert.Equal("\"node\" \"a\" \"bc\"".Length, EmitCheckPackage.CommandLineLength("node", ["a", "bc"]));
}
