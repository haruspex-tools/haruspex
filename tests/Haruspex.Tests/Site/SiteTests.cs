using System.Text.Json;
using System.Text.RegularExpressions;
using Haruspex.Cli;
using Haruspex.Cli.Output;
using Haruspex.Ir;
using Haruspex.Tests.Diagnostics;
using Haruspex.Tests.Documentation;

namespace Haruspex.Tests.Site;

/// <summary>
/// The site at haruspex.tools, as <c>site/build.mjs</c> builds it, serves exactly the addresses haruspex writes
/// (<see cref="HaruspexSite"/>): each one the command writes answers with a file, and each file is one a constant names. Built
/// from a copy of this repository's schemas and pages, committed in a new repository and tagged as release 1.2.3, so that the
/// addresses of <c>main</c> and of a release are both checked, offline and without this checkout's own history.
/// </summary>
public partial class SiteTests(BuiltSite built) : IClassFixture<BuiltSite>
{
    /// <summary>The release the copy is tagged as.</summary>
    internal const string Release = "1.2.3";

    /// <summary>The site, built on first use; a test that needs it is skipped where Node can't be run, as Node's own tests are.</summary>
    private BuiltSite site
    {
        get
        {
            EmitCheckPackage.RequireNode(RepositoryPaths.Root, "the site was not built");
            built.Build();
            return built;
        }
    }

    [Fact]
    public void The_site_script_passes_its_node_tests()
    {
        EmitCheckPackage.RequireNode(RepositoryPaths.Root, "the site script's tests were not run");

        // A glob, which Node expands itself (Node 21 and later), so the same argument works on every platform.
        Assert.True(
            EmitCheckPackage.TryRun("node", ["--test", "site/test/*.test.mjs"], RepositoryPaths.Root, out var output, out var why),
            $"node --test failed ({why}):\n{output}");
    }

    [Fact]
    public void The_site_serves_exactly_the_addresses_its_constants_name()
    {
        var expected = new List<string> { Path(HaruspexSite.Host) };
        foreach (var at in new[] { "main", "v" + Release })
            expected.AddRange(Directory.GetFiles(Repository("schemas"), "*.json").Select(f => Path($"{HaruspexSite.Schemas}{at}/{System.IO.Path.GetFileName(f)}")));
        expected.AddRange(Directory.GetFiles(Repository("schemas", "ir"), "*.schema.json").Select(f => Path(HaruspexSite.IrSchemas + System.IO.Path.GetFileName(f))));
        expected.AddRange(Pages(HaruspexSite.Diagnostics, "docs", "diagnostics"));
        expected.AddRange(Pages(HaruspexSite.Reasons, "docs", "reasons"));
        expected.AddRange(Pages(HaruspexSite.Docs, "docs"));

        Assert.Equal(expected.Order(StringComparer.Ordinal), site.Files);
    }

    [Fact]
    public void Each_schema_is_served_as_the_repository_holds_it()
    {
        foreach (var file in Directory.GetFiles(Repository("schemas"), "*.json"))
        {
            var name = System.IO.Path.GetFileName(file);
            Assert.Equal(File.ReadAllText(file), Served($"{HaruspexSite.Schemas}main/{name}"));
            Assert.Equal(File.ReadAllText(file), Served($"{HaruspexSite.Schemas}v{Release}/{name}"));
        }

        foreach (var file in Directory.GetFiles(Repository("schemas", "ir"), "*.schema.json"))
        {
            using var schema = JsonDocument.Parse(File.ReadAllText(file));
            var id = schema.RootElement.GetProperty("$id").GetString()!;
            Assert.Equal(File.ReadAllText(file), Served(id));
        }

        Assert.NotNull(Served(IrJsonSchema.Id));
    }

    [Fact]
    public void Each_page_leads_to_its_markdown_on_main()
    {
        foreach (var file in site.Files.Where(f => f.EndsWith("index.html", StringComparison.Ordinal)))
        {
            var target = Refresh().Match(File.ReadAllText(System.IO.Path.Combine(site.Out, file))).Groups["target"].Value;
            const string Main = "https://github.com/haruspex-tools/haruspex/blob/main/";
            if (file == "index.html")
            {
                Assert.Equal("https://github.com/haruspex-tools/haruspex", target);
            }
            else
            {
                Assert.StartsWith(Main, target, StringComparison.Ordinal);
                Assert.True(File.Exists(Repository(target[Main.Length..].Split('/'))), $"{file} leads to {target}, which is no file of the repository");
            }
        }
    }

    [Fact]
    public void Every_diagnostic_and_reason_page_is_served()
    {
        Assert.NotNull(Served(DiagnosticPages.Index));
        foreach (var (code, _) in DiagnosticCodesTests.Declared(DiagnosticCodesTests.Layers()))
            Assert.True(Served(DiagnosticPages.Of(code)) is not null, $"{DiagnosticPages.Of(code)} is not served");
        foreach (var code in ReasonCatalogueTests.Codes.Select(row => row.Data.Item1))
            Assert.True(Served(IrReasons.Page(code)) is not null, $"{IrReasons.Page(code)} is not served");
    }

    /// <summary>
    /// What the command writes, of a preview and of a release: <c>init</c>'s file, its help, each report and <c>explain</c>'s text and
    /// JSON. Every address in it is on the site, but for a standard's, and the site serves it.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Every_address_the_command_writes_is_served(bool release)
    {
        var terminal = new Terminal { Version = release ? $"{Release}+0123abcd00000000000000000000000000000000" : "0.0.0-preview-0123abcd+0123abcd00000000000000000000000000000000", IsRelease = release };
        using var folder = new TempDirectory();
        // A nullable column, for explain's page of a reason, and a table that isn't there, for a diagnostic.
        folder.File("db/t.sql", "CREATE TABLE dbo.t (a int NULL);");
        folder.File("db/p.sql", "CREATE PROCEDURE dbo.p AS SELECT t.a AS one, (SELECT name FROM dbo.missing) AS two FROM dbo.t AS t;");
        var written = new List<string>();
        string Run(params string[] args)
        {
            using var output = new StringWriter();
            HaruspexCli.Run(args, output, output, folder.Path, terminal);
            return output.ToString();
        }

        written.Add(Run("--help"));
        Run("init");
        written.Add(File.ReadAllText(System.IO.Path.Combine(folder.Path, "haruspex.json")));
        foreach (var format in new[] { "text", "json", "sarif" })
            written.Add(Run("generate", "db", "--include", "dbo.*", "--out", $"out-{format}", "--format", format));
        written.Add(Run("explain", "dbo.p", "one", "--ir", "out-text/haruspex.ir.json"));
        written.Add(Run("explain", "dbo.p", "one", "--ir", "out-text/haruspex.ir.json", "--format", "json"));

        var addresses = written.SelectMany(text => Address().Matches(text).Select(m => m.Value)).Distinct().ToList();
        Assert.Contains($"{HaruspexSite.Schemas}{(release ? "v" + Release : "main")}/{SchemaAddress.ConfigFile}", addresses);
        Assert.Contains($"{HaruspexSite.Schemas}{(release ? "v" + Release : "main")}/{SchemaAddress.Report}", addresses);
        Assert.Contains(HaruspexSite.Host, addresses);
        Assert.Contains(addresses, a => a.StartsWith(HaruspexSite.Diagnostics + "HAR", StringComparison.Ordinal));
        Assert.Contains(HaruspexSite.Reasons + "nullable-column", addresses);
        Assert.Contains(HaruspexSite.Docs + "configuration", addresses);
        Assert.Contains(HaruspexSite.Docs + "command-line", addresses);
        foreach (var address in addresses)
        {
            if (Standards.Contains(address))
                continue;
            Assert.True(address == HaruspexSite.Host || address.StartsWith(HaruspexSite.Host + "/", StringComparison.Ordinal), $"The command writes {address}, which is not on {HaruspexSite.Host}");
            Assert.True(Served(address) is not null, $"The command writes {address}, which the site doesn't serve");
        }
    }

    /// <summary>The standards' addresses the command writes, which it doesn't serve: SARIF's schema.</summary>
    private static readonly string[] Standards = ["https://docs.oasis-open.org/sarif/sarif/v2.1.0/errata01/os/schemas/sarif-schema-2.1.0.json"];

    /// <summary>The text the site answers <paramref name="address"/> with, or null when it serves none.</summary>
    private string? Served(string address)
    {
        var file = System.IO.Path.Combine(site.Out, Path(address));
        return File.Exists(file) ? File.ReadAllText(file) : null;
    }

    /// <summary>
    /// The file in the built site that answers <paramref name="address"/>: a schema by its name, and a page, whose address has no
    /// extension, by its folder's <c>index.html</c>.
    /// </summary>
    private static string Path(string address)
    {
        if (address == HaruspexSite.Host)
            return "index.html";
        Assert.StartsWith(HaruspexSite.Host + "/", address, StringComparison.Ordinal);
        var path = address[(HaruspexSite.Host.Length + 1)..];
        return path.EndsWith(".json", StringComparison.Ordinal) ? path : path.Length == 0 || path.EndsWith('/') ? path + "index.html" : path + "/index.html";
    }

    /// <summary>The page of each Markdown file directly in the folder, its README the folder's own.</summary>
    private static IEnumerable<string> Pages(string folder, params string[] repositoryFolder) =>
        Directory.GetFiles(Repository(repositoryFolder), "*.md")
            .Select(f => System.IO.Path.GetFileNameWithoutExtension(f))
            .Select(name => Path(name == "README" ? folder : folder + name));

    private static string Repository(params string[] parts) => System.IO.Path.Combine([RepositoryPaths.Root, .. parts]);

    [GeneratedRegex(@"https?://[^\s""'<>)\\␛]+")]
    private static partial Regex Address();

    [GeneratedRegex(@"<meta http-equiv=""refresh"" content=""0; url=(?<target>[^""]+)"">")]
    private static partial Regex Refresh();
}
