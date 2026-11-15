using System.Xml.Linq;
using Haruspex.Cli;
using Haruspex.Emit.Zod;
using Haruspex.Ir;
using Haruspex.SqlServer.Inference;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Sources.Dacpacs;
using Haruspex.SqlServer.Sources.Projects;

namespace Haruspex.Tests.Sources;

/// <summary>
/// The proof that a <c>.sqlproj</c> reads as the DACPAC its build makes: for each project, the model has exactly the package's
/// objects and settings, and the IR and Zod inferred from the project are byte for byte the package's, once each location is
/// rewritten as the same place in the package (<see cref="PackageLocationMap"/>) and the package's source record is set aside. The
/// packages are in <c>tests/dacpacs</c>, built by DacFx from the same project files (<c>build.sh</c>), each read with the packages
/// of its database it refers to, beside it as a build leaves them.
/// </summary>
public class ProjectEquivalenceTests
{
    private static string Packages => Path.Combine(RepositoryPaths.Root, "tests", "dacpacs");

    private static string Projects => Path.Combine(Packages, "projects");

    private static string WideWorldImporters =>
        Path.Combine(RepositoryPaths.Root, "tests", "corpus", "sql-server-samples", "samples", "databases", "wide-world-importers", "wwi-ssdt", "wwi-ssdt", "WideWorldImporters.sqlproj");

    /// <summary>The projects of our own and the package each builds, with the packages of its database beside it.</summary>
    public static TheoryData<string, string> Built { get; } = new()
    {
        { Path.Combine("orchard", "orchard.sqlproj"), Path.Combine("orchard", "orchard.dacpac") },
        { Path.Combine("mill", "mill.sqlproj"), Path.Combine("mill", "mill.dacpac") },
    };

    [Theory]
    [MemberData(nameof(Built))]
    public void A_project_gives_its_package_s_objects_settings_ir_and_zod(string project, string package)
    {
        var path = Path.Combine(Projects, project);
        var packagePath = Path.Combine(Packages, package);
        var read = SqlProjectReader.Read(path);
        var built = DacpacReader.Read(packagePath);
        Assert.Empty(read.Diagnostics);
        Assert.DoesNotContain(built.Diagnostics, d => d.IsError);
        AssertSameObjects(built.Model, read.Model);
        Assert.Equal(built.Model.Settings, read.Model.Settings);

        var fromProject = SqlProject.LoadProject(path);
        var fromPackage = SqlProject.LoadPackage(packagePath, []);
        var map = new PackageLocationMap(read.Model, Path.GetDirectoryName(path)!, [packagePath, .. built.References.Select(r => r.Found!.Path)]);
        foreach (var procedure in fromProject.Procedures)
            AssertSame(fromProject.Infer([procedure]).Expected(), fromPackage.Infer([procedure]).Expected(), map);
        Assert.Empty(map.Unmapped);
    }

    [Fact]
    public void WideWorldImporters_own_project_gives_the_ir_of_the_package_built_from_it()
    {
        var packagePath = Path.Combine(Packages, "corpus", "WideWorldImporters-ssdt.dacpac");
        var read = SqlProjectReader.Read(WideWorldImporters);
        var built = DacpacReader.Read(packagePath);
        Assert.DoesNotContain(read.Diagnostics, d => d.IsError);
        Assert.DoesNotContain(built.Diagnostics, d => d.IsError);
        AssertSameObjects(built.Model, read.Model);
        Assert.Equal(built.Model.Settings, read.Model.Settings);

        // Its Zod isn't compared: the corpus settles no names (two of its schemas have a procedure of the same name), and the Zod is
        // emitted from the IR alone, so the same IR gives the same Zod.
        var map = new PackageLocationMap(read.Model, Path.GetDirectoryName(WideWorldImporters)!, packagePath);
        var procedures = read.Model.Procedures.Select(p => p.Name).ToList();
        AssertSame(IrInference.Infer(read.Model, procedures).Value, IrInference.Infer(built.Model, procedures).Value, map, zod: false);
        Assert.Empty(map.Unmapped);
    }

    /// <summary>
    /// Every fixture that builds as a project, as the SDK-style project <c>build.sh</c> wrote to build its package (a name and a DSP,
    /// nothing else), read from a copy of its scripts: the default items and settings of a project that sets none.
    /// </summary>
    public static TheoryData<string> Fixtures { get; } =
        [.. Fixture.All.Where(f => !f.IsProject && File.Exists(Path.Combine(Packages, "fixtures", f.Name + ".dacpac"))).Select(f => f.Name)];

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void A_fixture_as_a_project_that_sets_nothing_gives_its_package_s_objects_settings_and_ir(string name)
    {
        var fixture = Fixture.Named(name);
        using var folder = new TempDirectory();
        foreach (var script in Directory.EnumerateFiles(fixture.Database, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(folder.Path, Path.GetRelativePath(fixture.Database, script));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(script, copy);
        }

        var project = folder.File($"{name}.sqlproj", $"""
            <Project Sdk="Microsoft.Build.Sql/2.3.0">
              <PropertyGroup>
                <Name>{name}</Name>
                <DSP>Microsoft.Data.Tools.Schema.Sql.Sql170DatabaseSchemaProvider</DSP>
              </PropertyGroup>
            </Project>
            """);
        var packagePath = Path.Combine(Packages, "fixtures", name + ".dacpac");
        var read = SqlProjectReader.Read(project);
        var built = DacpacReader.Read(packagePath);
        Assert.DoesNotContain(read.Diagnostics, d => d.IsError);
        AssertSameObjects(built.Model, read.Model, HistoryTables(packagePath));
        Assert.Equal(built.Model.Settings, read.Model.Settings);

        var config = fixture.Config is null ? HaruspexConfig.None(".") : ConfigReader.Read(fixture.Config).Expected();
        var fromProject = SqlProject.LoadProject(project);
        var fromPackage = SqlProject.LoadPackage(packagePath, []);
        var map = new PackageLocationMap(read.Model, folder.Path, packagePath);
        foreach (var procedure in fixture.Project.Procedures)
            AssertSame(fromProject.Infer([procedure], config).Expected(), fromPackage.Infer([procedure], config).Expected(), map);
        Assert.Empty(map.Unmapped);
    }

    /// <summary>
    /// The history tables a package holds that DacFx makes for a temporal table naming one no script creates
    /// (<c>WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = mart.price_history))</c>, in the <c>apply</c> fixture). A folder of the same
    /// scripts doesn't have them either, so a project's model lacks what no script says. No procedure of a fixture reads one, so
    /// the output is the same; a procedure that did would read an unknown table from the scripts, and a typed one from the package.
    /// </summary>
    private static string[] HistoryTables(string package)
    {
        using var zip = System.IO.Compression.ZipFile.OpenRead(package);
        using var stream = zip.GetEntry("model.xml")!.Open();
        XNamespace ns = "http://schemas.microsoft.com/sqlserver/dac/Serialization/2012/02";
        return [.. XDocument.Load(stream).Descendants(ns + "Relationship")
            .Where(r => (string?)r.Attribute("Name") == "TemporalSystemVersioningHistoryTable")
            .Descendants(ns + "References")
            .Select(r => "table " + ((string)r.Attribute("Name")!).Replace("[", "", StringComparison.Ordinal).Replace("]", "", StringComparison.Ordinal))];
    }

    /// <summary>
    /// Exactly the package's objects, by kind and name: what the build read, no more and no less, apart from the
    /// <paramref name="generated"/> ones the package has and no script declares.
    /// </summary>
    private static void AssertSameObjects(DatabaseModel package, DatabaseModel project, string[]? generated = null)
    {
        static IEnumerable<string> Objects(DatabaseModel model) =>
            model.Tables.Select(t => $"table {t.Name}")
                .Concat(model.Procedures.Select(p => $"procedure {p.Name}"))
                .Concat(model.Functions.Select(f => $"function {f.Name}"))
                .Concat(model.Views.Select(v => $"view {v.Name}"))
                .Concat(model.UserTypes.Select(u => $"type {u.Name}"))
                .Concat(model.Synonyms.Select(s => $"synonym {s.Name}"))
                .Concat(model.Triggers.Select(t => $"trigger {t.Name}"))
                .Order(StringComparer.Ordinal);

        Assert.Equal(Objects(package).Except(generated ?? []), Objects(project).Except(generated ?? []));
    }

    /// <summary>The two outputs equal, the project's locations rewritten and the package's source record set aside.</summary>
    private static void AssertSame(IrDocument fromProject, IrDocument fromPackage, PackageLocationMap map, bool zod = true)
    {
        Assert.Equal(map.Map(IrJsonWriter.Write(fromProject)), IrJsonWriter.Write(fromPackage with { Source = fromProject.Source }));
        if (zod)
            Assert.Equal(ZodEmitter.Emit(fromProject).Expected(), ZodEmitter.Emit(fromPackage with { Source = fromProject.Source }).Expected());
    }
}
