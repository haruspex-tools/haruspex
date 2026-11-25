using System.Text.RegularExpressions;
using Haruspex.Ir;

namespace Haruspex.Tests.Site;

/// <summary>
/// Every address haruspex writes into a file it doesn't own (a config file's <c>$schema</c>, a report's schema and help, a
/// schema's <c>$id</c>, a diagnostic's or a reason's page) is on <see cref="HaruspexSite.Host"/>, so that the repository can move
/// without any of them changing (docs/decisions/0121-every-address-haruspex-writes-is-served-from-haruspex-tools.md). The only other
/// addresses its code and schemas name are standards' own, which it doesn't serve.
/// </summary>
public partial class AddressTests
{
    /// <summary>The standards' addresses haruspex names: JSON Schema's dialect, SARIF's schema, and the namespace of a dacpac's model.</summary>
    private static readonly string[] Standards =
    [
        "https://json-schema.org/draft/2020-12/schema",
        "https://docs.oasis-open.org/sarif/sarif/v2.1.0/errata01/os/schemas/sarif-schema-2.1.0.json",
        "http://schemas.microsoft.com/sqlserver/dac/Serialization/2012/02",
    ];

    public static TheoryData<string> Files()
    {
        var root = RepositoryPaths.Root;
        var code = Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(p => p is "bin" or "obj"));
        var schemas = Directory.GetFiles(Path.Combine(root, "schemas"), "*.json", SearchOption.AllDirectories);
        return [.. code.Concat(schemas).Select(f => Path.GetRelativePath(root, f).Replace('\\', '/')).Order(StringComparer.Ordinal)];
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Every_address_in_the_code_and_the_schemas_is_on_haruspex_tools_or_a_standard_s(string file)
    {
        var text = File.ReadAllText(Path.Combine(RepositoryPaths.Root, file));
        var foreign = Address().Matches(text).Select(m => m.Value)
            .Where(a => !a.StartsWith(HaruspexSite.Host + "/", StringComparison.Ordinal) && a != HaruspexSite.Host && !Standards.Contains(a))
            .ToList();
        Assert.True(foreign.Count == 0, $"{file} names an address haruspex doesn't serve from {HaruspexSite.Host}: {string.Join(", ", foreign)}");
    }

    [Fact]
    public void The_host_is_haruspex_tools() => Assert.Equal("https://haruspex.tools", HaruspexSite.Host);

    [GeneratedRegex(@"https?://[^\s""'<>)\\{]+")]
    private static partial Regex Address();
}
