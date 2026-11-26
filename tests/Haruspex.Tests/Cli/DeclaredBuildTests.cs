using System.Text.Json.Nodes;
using Haruspex.Cli;

namespace Haruspex.Tests.Cli;

/// <summary>
/// The config file's <c>builds</c>, end to end: a package it names is read as a build, by the <c>.dacpac</c> route and by a
/// <c>.sqlproj</c> that refers to it; the IR records each package so read (<c>source.builds</c>, IR 0.31); and <c>check</c> reports
/// the declaration changing, naming the package. The package is <c>atelier-extract.dacpac</c>, extracted from a database in
/// which <c>pk_shelf</c>'s index was disabled, which the package can't say (decision 0120), so its key holds only on the user's word.
/// Why: docs/decisions/0122-the-ir-records-the-packages-declared-builds.md.
/// </summary>
public class DeclaredBuildTests
{
    private const string Extract = "atelier-extract.dacpac";

    private const string Declared = $$""" "builds": ["{{Extract}}"],""";

    /// <summary>A procedure of the project that finds a shelf by the referenced package's primary key.</summary>
    private const string ShelfLookup = "CREATE PROCEDURE studio.shelf_lookup @shelf_id int AS\nSELECT s.kiln_id FROM studio.shelf AS s WHERE s.shelf_id = @shelf_id;";

    /// <summary>A folder holding the extract and a config file that reads it, declared a build when <paramref name="builds"/> says so.</summary>
    private static TempDirectory FromPackage(string builds)
    {
        var folder = new TempDirectory();
        File.Copy(ExtractPath, Path.Combine(folder.Path, Extract));
        folder.File("haruspex.json", Config(Extract, builds, "shelf_by_id"));
        return folder;
    }

    /// <summary>
    /// A folder holding a project whose procedure reads the extract's table, the extract in <c>lib</c> where the project's
    /// <c>ArtifactReference</c> finds it, and a config file that reads the project.
    /// </summary>
    private static TempDirectory FromProject(string builds)
    {
        var folder = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(folder.Path, "lib"));
        File.Copy(ExtractPath, Path.Combine(folder.Path, "lib", Extract));
        folder.File("db/db.sqlproj", $"""
            <Project Sdk="Microsoft.Build.Sql/2.3.0">
              <PropertyGroup>
                <Name>db</Name>
                <DSP>Microsoft.Data.Tools.Schema.Sql.Sql160DatabaseSchemaProvider</DSP>
              </PropertyGroup>
              <ItemGroup><ArtifactReference Include="..\lib\{Extract}" /></ItemGroup>
            </Project>
            """);
        folder.File("db/procedures/shelf_lookup.sql", ShelfLookup);
        folder.File("haruspex.json", Config("db/db.sqlproj", builds, "shelf_lookup"));
        return folder;
    }

    private static string ExtractPath => Path.Combine(RepositoryPaths.Root, "tests", "dacpacs", "versions", Extract);

    private static string Config(string source, string builds, string procedure) =>
        $$"""{ "source": "{{source}}",{{builds}} "out": "out", "schemas": { "studio": { "include": ["{{procedure}}"] } } }""";

    private static (int ExitCode, string Output, string Error) Run(TempDirectory folder, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = HaruspexCli.Run(args, output, error, folder.Path);
        return (exitCode, CliOutput.Shown(output.ToString(), folder.Path), CliOutput.Shown(error.ToString(), folder.Path));
    }

    private static JsonNode Ir(TempDirectory folder) => JsonNode.Parse(File.ReadAllText(Path.Combine(folder.Path, "out", HaruspexCli.IrFileName)))!;

    private static string Cardinality(JsonNode ir) => ir["objects"]![0]!["results"]![0]!["cardinality"]!.GetValue<string>();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_package_declared_a_build_is_recorded_in_the_ir_as_one_read(bool declared)
    {
        using var folder = FromPackage(declared ? Declared : "");

        var (exitCode, _, error) = Run(folder, "generate");

        Assert.True(exitCode == 0, error);
        var ir = Ir(folder);
        Assert.Equal(declared ? "0.31" : "0.30", ir["irVersion"]!.GetValue<string>());
        Assert.Equal(declared ? """["atelier-extract.dacpac"]""" : null, ir["source"]!["builds"]?.ToJsonString());
        Assert.Equal(declared ? "atMostOne" : "many", Cardinality(ir));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Each_file_s_header_names_the_packages_declared_builds_only_when_there_are_some(bool declared)
    {
        // What the claims rest on is said where they are read, as a declared session is; with no build declared, nothing is said,
        // so every module written before is unchanged.
        const string Sentence = "Its types assume that atelier-extract.dacpac was built from a project, as the config file declares, so its primary keys and unique constraints hold.";
        using var folder = new TempDirectory();
        File.Copy(ExtractPath, Path.Combine(folder.Path, Extract));
        folder.File("haruspex.json", $$"""{ "source": "{{Extract}}",{{(declared ? Declared : "")}} "out": "out", "emit": ["zod", "json-schema", "mssql"], "schemas": { "studio": { "include": ["shelf_by_id"] } } }""");

        var (exitCode, _, error) = Run(folder, "generate");

        Assert.True(exitCode == 0, error);
        foreach (var file in new[] { HaruspexCli.ZodFileName, HaruspexCli.MssqlFileName, HaruspexCli.JsonSchemaFileName })
        {
            // A module's comment wraps a long line, which joined again reads as written.
            var text = File.ReadAllText(Path.Combine(folder.Path, "out", file)).ReplaceLineEndings("\n").Replace("\n// ", " ", StringComparison.Ordinal);
            Assert.True(declared == text.Contains(Sentence, StringComparison.Ordinal), $"{file}: {text[..Math.Min(text.Length, 1500)]}");
            Assert.True(declared == text.Contains("built from a project", StringComparison.Ordinal), file);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_project_s_reference_to_a_package_declared_a_build_keeps_its_keys_and_is_recorded(bool declared)
    {
        // The .sqlproj route end to end: builds passes from the config file through the project's read to the referenced package.
        using var folder = FromProject(declared ? Declared : "");

        var (exitCode, _, error) = Run(folder, "generate");

        Assert.True(exitCode == 0, error);
        var ir = Ir(folder);
        Assert.Equal(declared ? "atMostOne" : "many", Cardinality(ir));
        Assert.Equal(declared ? """["atelier-extract.dacpac"]""" : null, ir["source"]!["builds"]?.ToJsonString());
        Assert.Null(ir["source"]!["package"]);
    }

    [Theory]
    [InlineData(true, "no longer declared a build")]
    [InlineData(false, "now declared a build")]
    public void Check_fails_when_the_declaration_changes_naming_the_package(bool declaredFirst, string change)
    {
        using var folder = FromPackage(declaredFirst ? Declared : "");
        Assert.Equal(0, Run(folder, "generate").ExitCode);
        folder.File("haruspex.json", Config(Extract, declaredFirst ? "" : Declared, "shelf_by_id"));

        var (exitCode, _, error) = Run(folder, "check");

        Assert.Equal(1, exitCode);
        Assert.Contains($"source.builds[\"{Extract}\"]: {change}", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_fails_when_a_project_s_referenced_package_is_no_longer_declared_a_build()
    {
        using var folder = FromProject(Declared);
        Assert.Equal(0, Run(folder, "generate").ExitCode);
        folder.File("haruspex.json", Config("db/db.sqlproj", "", "shelf_lookup"));

        var (exitCode, _, error) = Run(folder, "check");

        Assert.Equal(1, exitCode);
        Assert.Contains($"source.builds[\"{Extract}\"]: no longer declared a build", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_package_declared_a_build_that_is_not_read_is_not_recorded_and_check_passes()
    {
        // Nothing was assumed of a package no source refers to, so declaring it changes no document.
        using var folder = FromPackage(Declared);
        Assert.Equal(0, Run(folder, "generate").ExitCode);
        folder.File("haruspex.json", Config(Extract, $$""" "builds": ["{{Extract}}", "elsewhere.dacpac"],""", "shelf_by_id"));

        var (exitCode, _, error) = Run(folder, "check");

        Assert.True(exitCode == 0, error);
        Assert.Equal("""["atelier-extract.dacpac"]""", Ir(folder)["source"]!["builds"]!.ToJsonString());
    }
}
