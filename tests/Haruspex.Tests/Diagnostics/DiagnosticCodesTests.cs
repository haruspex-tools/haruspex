using System.Reflection;
using System.Text;
using System.Xml.Linq;
using Haruspex.Diagnostics;
using Haruspex.Tests.Snapshotting;

namespace Haruspex.Tests.Diagnostics;

/// <summary>
/// Codes are stable (docs/decisions/0036-diagnostics-have-stable-codes.md): each layer declares its own, every one is unique
/// across the layers, and the whole list is a committed snapshot, so a code renumbered, reused or given another severity shows
/// in a diff a reviewer reads.
/// </summary>
public class DiagnosticCodesTests
{
    [Fact]
    public void Every_code_is_unique_across_the_layers()
    {
        var clashes = Clashes(Declared(Layers()));

        Assert.True(clashes.Count == 0, "Codes given out twice: " + string.Join("; ", clashes));
    }

    [Fact]
    public void The_list_of_codes_is_as_committed()
    {
        var list = new StringBuilder();
        foreach (var (code, where) in Declared(Layers()).OrderBy(c => c.Code.Number))
            list.Append(code).Append(' ').Append(code.Severity.ToString().ToUpperInvariant()[0]).Append(' ').Append(code.Title).Append(code.Retired ? " (retired)" : "").Append(" [").Append(where).Append("]\n");

        Snapshot.Match(list.ToString(), "DiagnosticCodesTests.Codes", "txt");
    }

    [Fact]
    public void A_code_declared_twice_is_found_wherever_it_is_declared()
    {
        var clashes = Clashes(Declared([.. Layers(), typeof(Planted).Assembly]).Where(c => c.Where.StartsWith("Haruspex.SqlServer.Sources", StringComparison.Ordinal) || c.Where.Contains(nameof(Planted), StringComparison.Ordinal)));

        Assert.Equal(["HAR1001: SourceCodes.ParseError, DiagnosticCodesTests+Planted.Clash"], clashes.Select(c => c.Replace("Haruspex.SqlServer.Sources.", "", StringComparison.Ordinal).Replace("Haruspex.Tests.Diagnostics.", "", StringComparison.Ordinal)));
    }

    /// <summary>A class of the test assembly that declares a code a layer has, as a new layer might.</summary>
    private static class Planted
    {
        public static DiagnosticCode Clash { get; } = new(1001, DiagnosticSeverity.Error, "A planted clash");
    }

    /// <summary>The assemblies of the layers: every project under <c>src/</c>, each a layer of docs/architecture.md (ArchitectureTests holds that).</summary>
    internal static IEnumerable<Assembly> Layers() =>
        Directory.EnumerateFiles(Path.Combine(RepositoryPaths.Root, "src"), "*.csproj", SearchOption.AllDirectories)
            .Select(file => XDocument.Load(file).Descendants("AssemblyName").Select(e => e.Value.Trim()).FirstOrDefault() ?? Path.GetFileNameWithoutExtension(file))
            .Order(StringComparer.Ordinal)
            .Select(Assembly.Load);

    /// <summary>Every code the assemblies declare, as a static property or field of any type, with where, as <c>Type.Member</c>.</summary>
    internal static List<(DiagnosticCode Code, string Where)> Declared(IEnumerable<Assembly> assemblies) =>
        [.. from assembly in assemblies
            from type in assembly.GetTypes()
            from member in type.GetMembers(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            let value = member switch
            {
                PropertyInfo { PropertyType: var t } property when t == typeof(DiagnosticCode) => property.GetValue(null),
                FieldInfo { FieldType: var t } field when t == typeof(DiagnosticCode) && !field.Name.Contains('<', StringComparison.Ordinal) => field.GetValue(null),
                _ => null,
            }
            where value is DiagnosticCode
            select ((DiagnosticCode)value, $"{type.FullName}.{member.Name}")];

    private static List<string> Clashes(IEnumerable<(DiagnosticCode Code, string Where)> codes) =>
        [.. codes.GroupBy(c => c.Code.Number).Where(g => g.Count() > 1).OrderBy(g => g.Key)
            .Select(g => $"{g.First().Code}: {string.Join(", ", g.Select(c => c.Where))}")];
}
