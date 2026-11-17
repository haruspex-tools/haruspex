namespace Haruspex.Tests.Packaging;

/// <summary>
/// The npm packages in <c>packaging/npm</c>: the launcher that runs a platform's binary and the script that packs the
/// tarballs. Their tests are JavaScript, run by Node's own test runner, so a change to either is tested wherever the rest
/// are, on every platform CI builds on.
/// </summary>
public class NpmPackagingTests
{
    [Fact]
    public void The_launcher_and_the_pack_script_pass_their_node_tests()
    {
        EmitCheckPackage.RequireNode(RepositoryPaths.Root, "the npm packages' tests were not run");

        // A glob, which Node expands itself (Node 21 and later), so the same argument works on every platform.
        Assert.True(
            EmitCheckPackage.TryRun("node", ["--test", "packaging/npm/test/*.test.mjs"], RepositoryPaths.Root, out var output, out var why),
            $"node --test failed ({why}):\n{output}");
    }
}
