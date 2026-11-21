using Haruspex.Cli;

namespace Haruspex.Tests.Examples;

/// <summary>
/// The example application in <c>examples/greenhouse</c>: its committed module is what haruspex generates for its database,
/// and its code compiles against that module, with the TypeScript and zod pinned in its own lockfile. CI runs both.
/// </summary>
public class ExampleAppTests
{
    private static readonly string Folder = Path.Combine(RepositoryPaths.Root, "examples", "greenhouse");

    [Fact]
    public void The_committed_module_is_what_haruspex_generates_for_the_example()
    {
        using var temp = new TempDirectory();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = HaruspexCli.Run(["generate", "--out", temp.Path], output, error, Folder);

        Assert.True(exitCode == 0, error.ToString());

        // Every file generate writes, its list of them included, and nothing else committed beside them.
        var written = Directory.GetFiles(temp.Path).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal).ToList();
        Assert.Equal(written, Directory.GetFiles(Path.Combine(Folder, "src", "db")).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        foreach (var file in written)
        {
            Assert.True(
                File.ReadAllText(Path.Combine(Folder, "src", "db", file)) == File.ReadAllText(Path.Combine(temp.Path, file)),
                $"examples/greenhouse/src/db/{file} is not what haruspex generates; run haruspex generate in examples/greenhouse.");
        }
    }

    [Fact]
    public void The_example_application_compiles_against_its_module()
    {
        EmitCheckPackage.RequireNode(Folder, "the example application was not compiled");
        EmitCheckPackage.EnsureInstalled(Folder);

        Assert.True(
            EmitCheckPackage.TryRun("node", [Path.Combine("node_modules", "typescript", "bin", "tsc"), "--noEmit", "-p", "."], Folder, out var output, out var why),
            $"tsc rejected the example application ({why}):\n{output}");
    }
}
