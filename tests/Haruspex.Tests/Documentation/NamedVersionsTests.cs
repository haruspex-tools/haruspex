using System.Text.Json;
using Haruspex.Emit.Zod;

namespace Haruspex.Tests.Documentation;

/// <summary>
/// The versions of zod, mssql and TypeScript the documentation names are the ones the tests compile the generated code with, pinned
/// in <c>tests/emit-check/package.json</c>, so that a reader told "these are known to work" is told the truth after an update.
/// </summary>
public class NamedVersionsTests
{
    [Theory]
    [InlineData("README.md", "zod {zod}, mssql {mssql} and TypeScript {typescript}")]
    [InlineData("docs/quick-start.md", "npm install zod@{zod}")]
    public void A_page_names_the_versions_the_emit_check_pins(string page, string text)
    {
        var expected = text
            .Replace("{zod}", Pinned("zod"), StringComparison.Ordinal)
            .Replace("{mssql}", Pinned("mssql"), StringComparison.Ordinal)
            .Replace("{typescript}", Pinned("typescript"), StringComparison.Ordinal);

        Assert.Contains(expected, File.ReadAllText(Path.Combine(RepositoryPaths.Root, page)), StringComparison.Ordinal);
    }

    /// <summary>
    /// The least zod the module is sound on is the one the emit check pins, so every check of what Zod accepts was observed on it,
    /// and each place a user reads it states the same one: the npm package's peer range, the README, the quick start and the
    /// header's note on lengths.
    /// </summary>
    [Fact]
    public void The_least_zod_is_the_pinned_one_and_every_place_states_it()
    {
        var least = ZodEmitter.LeastZod;

        Assert.Equal(Pinned("zod"), least);
        Assert.Contains($"export const leastZod = \"{least}\";", File.ReadAllText(Path.Combine(RepositoryPaths.Root, "packaging", "npm", "packages.mjs")), StringComparison.Ordinal);
        Assert.Contains("peerDependencies: { zod: `>=${leastZod} <5` }", File.ReadAllText(Path.Combine(RepositoryPaths.Root, "packaging", "npm", "packages.mjs")), StringComparison.Ordinal);
        Assert.Contains($"[Zod](https://zod.dev) {least} or a later 4.x", File.ReadAllText(Path.Combine(RepositoryPaths.Root, "README.md")), StringComparison.Ordinal);
        Assert.Contains($"The module needs zod {least} or a later 4.x", File.ReadAllText(Path.Combine(RepositoryPaths.Root, "docs", "quick-start.md")), StringComparison.Ordinal);
    }

    private static string Pinned(string package)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryPaths.EmitCheck, "package.json")));
        return json.RootElement.GetProperty("devDependencies").GetProperty(package).GetString()!;
    }
}
