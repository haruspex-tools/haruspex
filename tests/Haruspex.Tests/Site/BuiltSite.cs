namespace Haruspex.Tests.Site;

/// <summary>
/// The site built once for <see cref="SiteTests"/>, on first use: the repository's schemas and pages copied into a new repository,
/// committed, tagged <c>v1.2.3</c>, and built by <c>site/build.mjs</c>.
/// </summary>
public sealed class BuiltSite : IDisposable
{
    private readonly TempDirectory folder = new();

    private readonly Lock building = new();

    /// <summary>The built site's folder.</summary>
    public string Out => Path.Combine(folder.Path, "site");

    /// <summary>Every file of the site, relative to <see cref="Out"/>, with <c>/</c> between folders; empty until it is built.</summary>
    public IReadOnlyList<string> Files { get; private set; } = [];

    /// <summary>Builds the site, unless it is built already.</summary>
    public void Build()
    {
        lock (building)
        {
            if (Files.Count > 0)
                return;
            Files = Built();
        }
    }

    private List<string> Built()
    {
        var checkout = Path.Combine(folder.Path, "repo");
        foreach (var file in Directory.GetFiles(Path.Combine(RepositoryPaths.Root, "schemas"), "*", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(RepositoryPaths.Root, "docs"), "*.md"))
            .Concat(Directory.GetFiles(Path.Combine(RepositoryPaths.Root, "docs", "diagnostics"), "*.md"))
            .Concat(Directory.GetFiles(Path.Combine(RepositoryPaths.Root, "docs", "reasons"), "*.md")))
        {
            var copy = Path.Combine(checkout, Path.GetRelativePath(RepositoryPaths.Root, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy);
        }

        // git with no user settings, so that a signing key or a hook of the machine's can't change what the copy holds.
        var none = Path.Combine(folder.Path, "no-config");
        var git = new Dictionary<string, string> { ["GIT_CONFIG_GLOBAL"] = none, ["GIT_CONFIG_SYSTEM"] = none };
        string[] settings = ["-c", "user.name=Test", "-c", "user.email=test@example.invalid", "-c", "core.autocrlf=false"];
        foreach (var args in new[] { ["init", "-q", "-b", "main"], ["add", "-A"], ["commit", "-q", "-m", "files"], new[] { "tag", "v" + SiteTests.Release } })
            Assert.True(EmitCheckPackage.TryRun("git", [.. settings, .. args], checkout, git, out var output, out var why), $"git {string.Join(' ', args)} failed ({why}):\n{output}");

        Assert.True(
            EmitCheckPackage.TryRun("node", [Path.Combine(RepositoryPaths.Root, "site", "build.mjs"), checkout, Out], folder.Path, out var built, out var failed),
            $"site/build.mjs failed ({failed}):\n{built}");
        return [.. Directory.GetFiles(Out, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(Out, f).Replace('\\', '/')).Order(StringComparer.Ordinal)];
    }

    public void Dispose() => folder.Dispose();
}
