using Haruspex.Cli;
using Haruspex.Emit.Zod;
using Haruspex.Ir;
using Haruspex.SqlServer.Inference;
using Haruspex.SqlServer.Sources;
using Haruspex.SqlServer.Sources.Dacpacs;

namespace Haruspex.Tests.Sources;

/// <summary>
/// The proof that a DACPAC is read as its project's folder of scripts is: for every fixture that builds as a project, and for
/// WideWorldImporters, the IR and Zod inferred from the package are byte for byte those inferred from the folder, once each
/// location is rewritten as the same place in the package (<see cref="PackageLocationMap"/>) and the source record is set
/// aside, since only the package has one. The folder is read with the package's default collation, as a config file's
/// <c>collation</c> gives it, since only the package records one. The packages are in <c>tests/dacpacs</c>, built by DacFx from
/// the same scripts.
/// </summary>
public class DacpacEquivalenceTests
{
    private static string Packages => Path.Combine(RepositoryPaths.Root, "tests", "dacpacs");

    /// <summary>
    /// The fixtures that don't build as a project, each with the build error that keeps it out, which have no package: every other
    /// fixture that isn't a project of its own must have one, so a fixture added since the packages were built can't escape the
    /// proof in silence.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> Unbuilt = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Its scripts hold several statements in a batch (SQL71006) and a NOCHECK the build can't attach to an element (SQL71001).
        ["checks"] = "SQL71006",
        // Microsoft's install script, which declares the database and its files.
        ["adventure-works"] = "SQL70013",
        // Tables copied without what they refer to (SQL71501) and a statement a build doesn't take (SQL70001);
        // corpus/WideWorldImporters.dacpac is built from its own project, and compared below.
        ["wide-world-importers"] = "SQL71501",
    };

    /// <summary>Every fixture that isn't a project of its own, checked per procedure with the package built from it.</summary>
    public static TheoryData<string> Fixtures { get; } =
        [.. Fixture.All.Where(f => !f.IsProject && !Unbuilt.ContainsKey(f.Name)).Select(f => f.Name)];

    internal static string PackageOf(Fixture fixture) => Path.Combine(Packages, "fixtures", fixture.Name + ".dacpac");

    [Fact]
    public void Every_fixture_but_the_ones_that_don_t_build_has_a_package()
    {
        Assert.Empty(Fixture.All.Where(f => !f.IsProject && !Unbuilt.ContainsKey(f.Name) && !File.Exists(PackageOf(f))).Select(f => $"{f.Name} has no package: build it with tests/dacpacs/build.sh fixtures {f.Name}"));
        Assert.Empty(Unbuilt.Keys.Where(name => File.Exists(Path.Combine(Packages, "fixtures", name + ".dacpac"))).Select(name => $"{name} has a package now: take it off the list"));
    }

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

        var folder = SqlProject.Load(fixture.Database, package.DefaultCollation);
        foreach (var procedure in fixture.Project.Procedures)
            AssertSame(folder.Infer([procedure], config).Expected(), package.Infer([procedure], config).Expected(), map);
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

        var folder = SqlProject.Load(fixture.Database, package.DefaultCollation);
        foreach (var procedure in fixture.Project.Procedures)
            AssertSame(folder.Infer([procedure]).Expected(), package.Infer([procedure]).Expected(), map);
        Assert.Empty(map.Unmapped);
    }

    [Fact]
    public void WideWorldImporters_package_gives_the_ir_and_zod_of_its_project_s_scripts()
    {
        var corpus = Corpus.Corpus.All.Single(c => c.Name == "WideWorldImporters");
        var packagePath = Path.Combine(Packages, "corpus", "WideWorldImporters.dacpac");
        var package = DacpacReader.Read(packagePath);
        var folder = SqlScriptFolder.Read(corpus.ProjectFolder, corpus.Scripts).Model.WithDeclaredCollation(package.Model.DefaultCollation);
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
