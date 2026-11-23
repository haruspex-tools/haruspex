using System.Text.Json;

namespace Haruspex.Tests.Packaging;

/// <summary>
/// The licences of the libraries compiled into the native binary, committed in <c>packaging/licenses</c> because their
/// packages hold no licence file (<c>packaging/licenses/README.md</c>). The publish copies them beside the binary and the
/// pack script refuses a binary without them, so what is committed here is what every copy carries; these tests keep it
/// in step with the packages the command really depends on.
/// </summary>
public class LicenseTests
{
    private static string Licenses => Path.Combine(RepositoryPaths.Root, "packaging", "licenses");

    [Fact]
    // The lock file lists every package in the command's graph, the transitive ones too, at the version restored: the
    // libraries the binary is compiled from. A version or a package that changes there changes what must be committed.
    public void The_committed_licenses_are_those_of_the_packages_the_command_ships() =>
        Assert.Equal(LockedPackages(), CommittedFolders());

    [Fact]
    public void Each_committed_folder_holds_a_license_and_says_where_it_came_from()
    {
        var readme = File.ReadAllText(Path.Combine(Licenses, "README.md"));
        foreach (var folder in CommittedFolders())
        {
            Assert.Contains(
                Directory.EnumerateFiles(Path.Combine(Licenses, folder)),
                file => Path.GetFileName(file).StartsWith("LICENSE", StringComparison.Ordinal));
            Assert.Contains($"`{folder}`", readme, StringComparison.Ordinal);
        }
    }

    /// <summary>The command's packages, as NuGet names their folders: <c>&lt;id&gt;.&lt;version&gt;</c>.</summary>
    private static string[] LockedPackages()
    {
        using var lockFile = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(RepositoryPaths.Root, "src", "Haruspex.Cli", "packages.lock.json")));
        // Only the framework's own section: an AOT publish adds one per runtime identifier, never committed, whose extra
        // packages (the compiler, the runtime pack) carry their own licence files, which the publish copies.
        var framework = lockFile.RootElement.GetProperty("dependencies").GetProperty("net10.0");
        return [.. framework.EnumerateObject()
            .Where(package => package.Value.GetProperty("type").GetString() != "Project")
            .Select(package => $"{package.Name}.{package.Value.GetProperty("resolved").GetString()}")
            .Order(StringComparer.Ordinal)];
    }

    private static string[] CommittedFolders() =>
        [.. Directory.EnumerateDirectories(Licenses).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)];
}
