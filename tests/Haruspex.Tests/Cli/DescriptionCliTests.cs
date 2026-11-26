using Haruspex.Cli;

namespace Haruspex.Tests.Cli;

/// <summary>
/// The config file's <c>descriptions</c> reaches the reader of every kind of source: a folder, a project file and a package each
/// read the names it gives, and no others (docs/decisions/0128-a-column-s-description-reaches-the-fields-that-are-that-column.md).
/// </summary>
public class DescriptionCliTests
{
    private static readonly string Lectern = Path.Combine(RepositoryPaths.Fixtures, "lectern", "database");

    private static readonly string LecternPackage = Path.Combine(RepositoryPaths.Root, "tests", "dacpacs", "fixtures", "lectern.dacpac");

    /// <summary>A sentence of the lectern fixture's: the description of <c>library.book.title</c>.</summary>
    private const string Title = ".describe(\"The title as printed";

    /// <summary>Runs <c>generate</c> in <paramref name="folder"/> with its config file, and returns the Zod module it wrote.</summary>
    private static string Generate(TempDirectory folder, string source, string? descriptions)
    {
        var setting = descriptions is null ? "" : $", \"descriptions\": {descriptions}";
        folder.File("haruspex.json", $$"""{ "source": "{{source}}", "out": "out", "schemas": { "library": { "include": ["find_book"] } }{{setting}} }""");
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.True(HaruspexCli.Run(["generate"], output, error, folder.Path, FixedBuild.Terminal) == 0, error.ToString());
        return File.ReadAllText(Path.Combine(folder.Path, "out", HaruspexCli.ZodFileName));
    }

    /// <summary>A copy of the lectern fixture's scripts with every <c>MS_Description</c> renamed <c>Description</c>, as some projects name it.</summary>
    private static void Renamed(TempDirectory folder, string into)
    {
        foreach (var file in Directory.EnumerateFiles(Lectern, "*.sql", SearchOption.AllDirectories))
            folder.File(Path.Combine(into, Path.GetRelativePath(Lectern, file)), File.ReadAllText(file).Replace("N'MS_Description'", "N'Description'", StringComparison.Ordinal));
    }

    [Fact]
    public void A_folder_reads_the_names_descriptions_gives_and_only_those()
    {
        using var folder = new TempDirectory();
        Renamed(folder, "db");

        Assert.DoesNotContain(Title, Generate(folder, "db", null), StringComparison.Ordinal);
        Assert.Contains(Title, Generate(folder, "db", "[\"Description\"]"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_project_file_reads_the_names_descriptions_gives()
    {
        using var folder = new TempDirectory();
        Renamed(folder, "db");
        folder.File("db/lectern.sqlproj", """
            <Project Sdk="Microsoft.Build.Sql/2.3.0">
              <PropertyGroup>
                <Name>lectern</Name>
                <DSP>Microsoft.Data.Tools.Schema.Sql.Sql170DatabaseSchemaProvider</DSP>
              </PropertyGroup>
            </Project>
            """);

        Assert.DoesNotContain(Title, Generate(folder, "db/lectern.sqlproj", null), StringComparison.Ordinal);
        Assert.Contains(Title, Generate(folder, "db/lectern.sqlproj", "[\"Description\"]"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_package_reads_the_names_descriptions_gives_and_none_for_an_empty_list()
    {
        using var folder = new TempDirectory();
        File.Copy(LecternPackage, Path.Combine(folder.Path, "lectern.dacpac"));

        Assert.Contains(Title, Generate(folder, "lectern.dacpac", null), StringComparison.Ordinal);
        Assert.DoesNotContain(".describe(", Generate(folder, "lectern.dacpac", "[]"), StringComparison.Ordinal);
    }
}
