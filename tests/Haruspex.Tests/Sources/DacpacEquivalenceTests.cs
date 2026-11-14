using Haruspex.Cli;
using Haruspex.Emit.Zod;
using Haruspex.Ir;
using Haruspex.SqlServer.Inference;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Sources;
using Haruspex.SqlServer.Sources.Dacpacs;

namespace Haruspex.Tests.Sources;

/// <summary>
/// The proof that a DACPAC is read as its project's folder of scripts is: for every fixture that builds as a project, and for
/// WideWorldImporters, the IR and Zod inferred from the package are byte for byte those inferred from the folder, once each
/// location is rewritten as the same place in the package (<see cref="PackageLocationMap"/>) and the source record is set
/// aside, since only the package has one. The packages are in <c>tests/dacpacs</c>, built by DacFx from the same scripts.
/// </summary>
public class DacpacEquivalenceTests
{
    private static string Packages => Path.Combine(RepositoryPaths.Root, "tests", "dacpacs");

    /// <summary>Every fixture checked per procedure with a package built from it.</summary>
    public static TheoryData<string> Fixtures { get; } =
        [.. Fixture.All.Where(f => !f.IsProject && File.Exists(PackageOf(f))).Select(f => f.Name)];

    private static string PackageOf(Fixture fixture) => Path.Combine(Packages, "fixtures", fixture.Name + ".dacpac");

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void A_fixture_s_package_gives_its_folder_s_ir_and_zod_for_every_procedure(string name)
    {
        var fixture = Fixture.Named(name);
        var package = SqlProject.LoadPackage(PackageOf(fixture), []);
        Assert.DoesNotContain(package.Diagnostics, d => d.IsError);
        Assert.Equal(fixture.Project.Procedures.Select(p => p.ToString()), package.Procedures.Select(p => p.ToString()));
        var config = fixture.Config is null ? HaruspexConfig.None(".") : ConfigReader.Read(fixture.Config).Expected();
        var map = new PackageLocationMap(SqlScriptFolder.Read(fixture.Database).Model, fixture.Database, PackageOf(fixture));

        foreach (var procedure in fixture.Project.Procedures)
            AssertSame(fixture.Project.Infer([procedure], config).Expected(), package.Infer([procedure], config).Expected(), map);
        Assert.Empty(map.Unmapped);
    }

    /// <summary>The orders fixture built by older versions of Microsoft.Build.Sql, whose model.xml DacFx wrote differently.</summary>
    public static TheoryData<string> Versions { get; } = ["orders-build-sql-1.0.0.dacpac", "orders-build-sql-2.0.0.dacpac", "orders-extract.dacpac"];

    [Theory]
    [MemberData(nameof(Versions))]
    public void The_orders_fixture_built_by_another_version_or_extracted_gives_its_folder_s_ir_and_zod(string file)
    {
        var fixture = Fixture.Named("orders");
        var path = Path.Combine(Packages, "versions", file);
        var package = SqlProject.LoadPackage(path, []);
        Assert.DoesNotContain(package.Diagnostics, d => d.IsError);
        var map = new PackageLocationMap(SqlScriptFolder.Read(fixture.Database).Model, fixture.Database, path);

        foreach (var procedure in fixture.Project.Procedures)
            AssertSame(fixture.Project.Infer([procedure]).Expected(), package.Infer([procedure]).Expected(), map);
        Assert.Empty(map.Unmapped);
    }

    [Fact]
    public void A_name_in_another_database_is_unknown_from_a_package_as_from_its_project_s_scripts()
    {
        // harbour reads [$(Tally)].ledger.entry: the reference to tally is found, but another database's names aren't bound
        // yet, and the variable is not taken for a database's name.
        using var dir = new TempDirectory();
        foreach (var project in new[] { "quay", "crane", "harbour" })
        {
            foreach (var script in Directory.EnumerateFiles(Path.Combine(Packages, "projects", project), "*.sql"))
                File.Copy(script, Path.Combine(dir.Path, project + "-" + Path.GetFileName(script)));
        }

        var name = new ObjectName("dock", "crane_fees");
        var fromFolder = SqlProject.Load(dir.Path).Infer([name]).Expected();
        var fromPackage = SqlProject.LoadPackage(Path.Combine(Packages, "harbour", "harbour.dacpac"), []).Infer([name]).Expected();

        var expected = fromFolder.Unknowns.Select(u => (u.Path, u.Reason, u.Detail));
        Assert.Equal(expected, fromPackage.Unknowns.Select(u => (u.Path, u.Reason, u.Detail)));
        Assert.Contains(fromPackage.Unknowns, u => u.Detail.Contains("[$(Tally)].ledger.entry", StringComparison.Ordinal));
    }

    [Fact]
    public void WideWorldImporters_package_gives_the_ir_and_zod_of_its_project_s_scripts()
    {
        var corpus = Corpus.Corpus.All.Single(c => c.Name == "WideWorldImporters");
        var folder = SqlScriptFolder.Read(corpus.ProjectFolder, corpus.Scripts).Model;
        var packagePath = Path.Combine(Packages, "corpus", "WideWorldImporters.dacpac");
        var package = DacpacReader.Read(packagePath);
        Assert.DoesNotContain(package.Diagnostics, d => d.IsError);
        var map = new PackageLocationMap(folder, corpus.ProjectFolder, packagePath);
        var procedures = folder.Procedures.Select(p => p.Name).ToList();

        // Its Zod isn't compared: the corpus settles no names (two of its schemas have a procedure of the same name), and the
        // Zod is emitted from the IR alone, so the same IR gives the same Zod.
        AssertSame(IrInference.Infer(folder, procedures).Value, IrInference.Infer(package.Model, procedures).Value, map, zod: false);
        Assert.Empty(map.Unmapped);
    }

    /// <summary>
    /// The two outputs equal, the folder's locations rewritten and the package's source record set aside. With
    /// <paramref name="zod"/>, the Zod emitted from each too. Nothing else is rewritten: a type is spelt with its defaults however
    /// it was read (<c>docs/decisions/0041-a-type-is-spelt-with-its-defaults-written-out.md</c>).
    /// </summary>
    private static void AssertSame(IrDocument fromFolder, IrDocument fromPackage, PackageLocationMap map, bool zod = true)
    {
        Assert.Equal(map.Map(IrJsonWriter.Write(fromFolder)), IrJsonWriter.Write(fromPackage with { Source = fromFolder.Source }));
        if (zod)
            Assert.Equal(ZodEmitter.Emit(fromFolder).Expected(), ZodEmitter.Emit(fromPackage with { Source = fromFolder.Source }).Expected());
    }
}
