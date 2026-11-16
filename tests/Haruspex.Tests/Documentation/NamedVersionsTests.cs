using System.Text.Json;

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

    private static string Pinned(string package)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryPaths.EmitCheck, "package.json")));
        return json.RootElement.GetProperty("devDependencies").GetProperty(package).GetString()!;
    }
}
