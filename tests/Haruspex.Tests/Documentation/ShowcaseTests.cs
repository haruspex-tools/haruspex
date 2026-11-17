using System.Globalization;
using System.Text.RegularExpressions;
using Haruspex.Cli;
using Haruspex.Emit.Mssql;
using Haruspex.Emit.Zod;
using Haruspex.Ir;
using SampleCorpus = Haruspex.Tests.Corpus.Corpus;

namespace Haruspex.Tests.Documentation;

/// <summary>
/// The project README's showcase: an example over each of Microsoft's vendored samples, written for the README over the
/// sample's own tables. <see cref="WorkedExampleTests"/> holds each to what haruspex infers over the whole sample. These tests
/// hold each to a real server too: its SQL is a fixture the differential tests deploy, seed and call, and that fixture's
/// snapshot is the README's Zod, so the fixture's copy of the sample's tables types the example as the sample does. The
/// function the README shows beside the Zod is called on the server too, as every fixture's procedures are
/// (<c>GeneratedFunctionTests</c>). A query file in the showcase is a file of the same fixture, in its <c>sql/</c> folder, typed
/// against the fixture's tables to the Zod and the function the README shows, and called on the server through that function
/// by the fixture's runs. And each call the README shows, in a <c>ts haruspex-call</c> block, is compiled strictly against the
/// module its fixture generates, so a call that names a parameter or a function wrongly fails as it would in an application.
/// </summary>
public partial class ShowcaseTests
{
    private const string Readme = "README.md";

    /// <summary>The folders of a fixture's database that hold the objects an example defines.</summary>
    private static readonly string[] CodeFolders = ["functions", "procedures"];

    private static readonly List<WorkedExample> Showcase =
    [
        .. WorkedExample.Read(Readme, File.ReadAllText(Path.Combine(RepositoryPaths.Root, Readme))).Examples.Where(e => e.Over is not null),
    ];

    public static TheoryData<int> Examples { get; } = [.. Showcase.Where(e => e.Query is null).Select(e => e.Line)];

    public static TheoryData<int> Queries { get; } = [.. Showcase.Where(e => e.Query is not null).Select(e => e.Line)];

    /// <summary>The mark of a block holding a call of the showcase's functions, which imports them from <see cref="FunctionsModule"/>.</summary>
    private const string CallMark = "haruspex-call";

    /// <summary>Where a call block imports the functions from: the module's functions' file, as an application's <c>db</c> folder holds it.</summary>
    private const string FunctionsModule = "./db/haruspex.mssql";

    private static readonly List<MarkdownText.FencedBlock> Calls =
    [
        .. MarkdownText.FencedBlocks(File.ReadAllText(Path.Combine(RepositoryPaths.Root, Readme))).Where(b => b.Words is ["ts", CallMark, ..]),
    ];

    public static TheoryData<int> CallLines { get; } = [.. Calls.Select(c => c.Line)];

    [Fact]
    public void The_readme_shows_an_example_over_each_vendored_sample() =>
        Assert.Equal(SampleCorpus.All.Select(c => c.Name).Order(StringComparer.Ordinal), Showcase.Where(e => e.Query is null).Select(e => e.Over).Order(StringComparer.Ordinal));

    [Fact]
    public void The_readme_shows_a_query_file_with_its_function_beside_its_zod() =>
        Assert.Contains(Showcase, e => e.Query is not null && e.Outputs.Any(o => o.Kind == WorkedExample.OutputKind.Mssql) && e.Outputs.Any(o => o.Kind == WorkedExample.OutputKind.Zod));

    [Fact]
    public void The_readme_shows_the_function_generated_for_a_showcase_example_beside_its_zod() =>
        Assert.Contains(Showcase, e => e.Outputs.Any(o => o.Kind == WorkedExample.OutputKind.Mssql) && e.Outputs.Any(o => o.Kind == WorkedExample.OutputKind.Zod));

    [Theory]
    [MemberData(nameof(Examples))]
    public void Each_showcase_example_is_a_deployed_fixture_holding_exactly_its_sql(int line)
    {
        var example = Showcase.Single(e => e.Line == line);
        var fixture = FixtureOf(example);

        Assert.True(fixture.IsDeployed, $"The fixture {fixture.Name} isn't deployed, so the example at {Readme}:{At(example)} is never run.");
        Assert.Equal(Batches(example.Sql).Order(StringComparer.Ordinal), Code(fixture).Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Examples))]
    public void Each_showcase_example_s_fixture_generates_the_zod_the_readme_shows(int line)
    {
        var example = Showcase.Single(e => e.Line == line);
        var shown = Assert.Single(example.Outputs, o => o.Kind == WorkedExample.OutputKind.Zod);
        var (_, zod) = Assert.Single(FixtureOf(example).Snapshots);

        Assert.Equal(Text(shown.Text), Text(WorkedExample.ZodShown(File.ReadAllText(zod), shown.Text)));
    }

    [Theory]
    [MemberData(nameof(Queries))]
    public void Each_showcase_query_is_a_file_of_a_deployed_fixture_holding_exactly_its_sql(int line)
    {
        var example = Showcase.Single(e => e.Line == line);
        var (fixture, file) = QueryOf(example);

        Assert.True(fixture.IsDeployed, $"The fixture {fixture.Name} isn't deployed, so the query at {Readme}:{At(example)} is never run.");
        Assert.Equal(Text(example.Sql), Text(File.ReadAllText(file)));
    }

    [Theory]
    [MemberData(nameof(Queries))]
    public void Each_showcase_query_s_fixture_generates_the_zod_and_the_function_the_readme_shows(int line)
    {
        var example = Showcase.Single(e => e.Line == line);
        var (fixture, _) = QueryOf(example);
        var module = InferQuery.Module(fixture.Folder, fixture.Database, example.Query!);
        var problems = new List<string>();
        foreach (var shown in example.Outputs)
        {
            var generated = shown.Kind switch
            {
                WorkedExample.OutputKind.Zod => WorkedExample.ZodShown(ZodEmitter.Emit(module).Expected(), shown.Text),
                WorkedExample.OutputKind.Mssql => WorkedExample.MssqlShown(Functions(module), shown.Text),
                _ => throw new InvalidOperationException($"{Readme}:{shown.Line.ToString(CultureInfo.InvariantCulture)}: the showcase shows a query's Zod and function, not its {shown.Kind}."),
            };
            if (Text(generated) != Text(shown.Text))
                problems.Add($"{Readme}:{shown.Line.ToString(CultureInfo.InvariantCulture)}: the fixture {fixture.Name} gives\n{generated}");
        }

        Assert.True(problems.Count == 0, string.Join("\n\n", problems));
    }

    [Fact]
    public void The_readme_s_call_of_a_procedure_and_of_a_query_file_are_compiled()
    {
        var called = Calls.SelectMany(c => Imported(c.Text)).ToHashSet(StringComparer.Ordinal);

        Assert.Contains(Showcase, e => e.Query is null && e.Outputs.Any(o => o.Kind == WorkedExample.OutputKind.Mssql && Defines(o.Text, called)));
        Assert.Contains(Showcase, e => e.Query is not null && e.Outputs.Any(o => o.Kind == WorkedExample.OutputKind.Mssql && Defines(o.Text, called)));
    }

    /// <summary>
    /// The call compiles with the emit check's <c>tsc</c> and its strict settings, against the Zod and the functions generated
    /// for every procedure and query file of the one showcase fixture that defines what it imports, with <c>pool</c> an mssql
    /// <c>ConnectionPool</c>: so a misspelt key, a missing one or a value of the wrong type fails, as the parameters' type says.
    /// </summary>
    [Theory]
    [MemberData(nameof(CallLines))]
    public void Each_call_the_readme_shows_compiles_against_its_fixture_s_module(int line)
    {
        var call = Calls.Single(c => c.Line == line);
        var imported = Imported(call.Text);
        Assert.True(imported.Count > 0, $"The call at {Readme}:{At(line)} imports nothing from '{FunctionsModule}'.");
        var document = Assert.Single(
            Showcase.Select(e => e.Query is null ? FixtureOf(e) : QueryOf(e).Fixture).Distinct().Select(Module),
            m => imported.All(name => m.Objects.Any(o => MssqlEmitter.FunctionName(o.Name) == name)));

        EmitCheckPackage.RequireNode(RepositoryPaths.EmitCheck, "the README's calls were not compiled");
        EmitCheckPackage.EnsureInstalled(RepositoryPaths.EmitCheck);
        var folder = Path.Combine(RepositoryPaths.EmitCheck, "accept", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "db"));
        try
        {
            File.WriteAllText(Path.Combine(folder, "db", "haruspex.zod.ts"), ZodEmitter.Emit(document).Expected());
            File.WriteAllText(Path.Combine(folder, "db", "haruspex.mssql.ts"), Functions(document));
            File.WriteAllText(Path.Combine(folder, "call.ts"), "import type sql from 'mssql'\ndeclare const pool: sql.ConnectionPool\n" + call.Text + "\n");
            File.WriteAllText(Path.Combine(folder, "tsconfig.json"), "{ \"extends\": \"../../tsconfig.json\", \"include\": [\"**/*.ts\"] }");

            Assert.True(
                EmitCheckPackage.TryRun("node", [Path.Combine("..", "..", "node_modules", "typescript", "bin", "tsc"), "--noEmit", "-p", "."], folder, out var output, out var why),
                $"tsc rejected the call at {Readme}:{At(line)} ({why}):\n{output}");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// What the fixture's application would generate: every procedure, and its query files as <c>"queries": "sql"</c> would type
    /// them, in one module.
    /// </summary>
    private static IrDocument Module(Fixture fixture) =>
        fixture.Queries is null ? fixture.Ir : InferQuery.Module(fixture.Folder, fixture.Database, Fixture.QueryFolder, withProcedures: true);

    /// <summary>The names a call block imports from <see cref="FunctionsModule"/>.</summary>
    private static List<string> Imported(string call) =>
    [
        .. ImportLine().Matches(call)
            .Where(m => m.Groups["from"].Value == FunctionsModule)
            .SelectMany(m => m.Groups["names"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)),
    ];

    /// <summary>Whether a functions block shown defines one of <paramref name="names"/>.</summary>
    private static bool Defines(string functions, IReadOnlySet<string> names) =>
        names.Any(n => functions.Contains($"export async function {n}(", StringComparison.Ordinal));

    /// <summary>The fixture holding the query example's file at its path, and that file.</summary>
    private static (Fixture Fixture, string File) QueryOf(WorkedExample example) =>
        Fixture.All
            .Where(f => f.Queries is not null && File.Exists(Path.Combine(f.Folder, example.Query!)))
            .Select(f => (f, Path.Combine(f.Folder, example.Query!)))
            .SingleOrDefault() is ({ } fixture, { } file)
            ? (fixture, file)
            : throw new InvalidOperationException($"No fixture holds the query file {example.Query} of the example at {Readme}:{At(example)}.");

    /// <summary>The database module's functions, written as <c>haruspex generate</c> writes them beside the Zod module.</summary>
    private static string Functions(IrDocument document) =>
        MssqlEmitter.Emit(document, new MssqlOptions { SchemaModule = HaruspexCli.ZodModuleSpecifier, IsReserved = TypeScriptNames.IsReserved }).Expected();

    /// <summary>The fixture whose functions and procedures include the example's first object.</summary>
    private static Fixture FixtureOf(WorkedExample example)
    {
        var first = Batches(example.Sql)[0];
        return Fixture.All.SingleOrDefault(f => Code(f).Contains(first))
            ?? throw new InvalidOperationException($"No fixture holds the SQL of the example at {Readme}:{At(example)}.");
    }

    /// <summary>The text of each function and procedure in the fixture, one object to a file.</summary>
    private static List<string> Code(Fixture fixture) =>
    [
        .. CodeFolders
            .Select(folder => Path.Combine(fixture.Database, folder))
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.sql"))
            .Select(file => Text(File.ReadAllText(file))),
    ];

    /// <summary>The script's batches, split on <c>GO</c> lines as sqlcmd splits them.</summary>
    private static List<string> Batches(string sql) => [.. GoLine().Split(sql).Select(Text).Where(b => b.Length > 0)];

    private static string Text(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

    private static string At(WorkedExample example) => At(example.Line);

    private static string At(int line) => line.ToString(CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^import \{(?<names>[^}]*)\} from '(?<from>[^']+)'", RegexOptions.Multiline)]
    private static partial Regex ImportLine();

    [GeneratedRegex(@"^[ \t]*GO[ \t]*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex GoLine();
}
