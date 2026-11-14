using System.Text.RegularExpressions;
using Haruspex.Cli;
using Haruspex.Emit.Zod;
using Haruspex.Ir;
using Haruspex.SqlServer.Inference;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;
using Haruspex.SqlServer.Sources;
using Haruspex.SqlServer.Sources.Dacpacs;

namespace Haruspex.Tests.Sources;

/// <summary>
/// The proof that a DACPAC is read as its project's folder of scripts is: for every fixture that builds as a project, and for
/// WideWorldImporters, the IR and Zod inferred from the package are byte for byte those inferred from the folder, once each
/// location is rewritten as the same place in the package (<see cref="PackageLocationMap"/>) and the source record is set
/// aside, since only the package has one. The packages are in <c>tests/dacpacs</c>, built by DacFx from the same scripts.
/// </summary>
public partial class DacpacEquivalenceTests
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
    /// The two outputs equal, the folder's locations rewritten, the package's source record set aside, and each fact's type
    /// source spelt with its defaults (see <see cref="Canonical"/>). With <paramref name="zod"/>, the Zod emitted from each too.
    /// </summary>
    private static void AssertSame(IrDocument fromFolder, IrDocument fromPackage, PackageLocationMap map, bool zod = true)
    {
        var expected = Canonical(map.Map(IrJsonWriter.Write(fromFolder)));
        var actual = Canonical(IrJsonWriter.Write(fromPackage with { Source = fromFolder.Source }));
        Assert.Equal(expected, actual);
        if (zod)
            Assert.Equal(Canonical(ZodEmitter.Emit(fromFolder).Expected()), Canonical(ZodEmitter.Emit(fromPackage with { Source = fromFolder.Source }).Expected()));
    }

    /// <summary>
    /// The text with each built-in type a fact is attributed to (<c>"type": "decimal"</c>) spelt with its defaults
    /// (<c>decimal(18,0)</c>), through Semantics' own reading of a type. A fact's type source is the type as written, and a
    /// package holds the type DacFx resolved, not its spelling, so <c>decimal</c> and <c>decimal(18,0)</c> are one type written two
    /// ways. Nothing else is rewritten, and a type that resolves differently stays different.
    /// </summary>
    private static string Canonical(string text) => TypeSource().Replace(text, match =>
    {
        var arguments = match.Groups["arguments"].Success ? match.Groups["arguments"].Value.Split(',') : [];
        var type = new SqlType(SqlTypeKind.BuiltIn, match.Groups["name"].Value, [.. arguments]);
        return ScalarType.Of(type) is { } scalar ? $"\"type\": \"{scalar.Declaration}\"" : match.Value;
    });

    [GeneratedRegex(@"""type"": ""(?<name>[a-z0-9]+)(?:\((?<arguments>[^)]*)\))?""")]
    private static partial Regex TypeSource();
}
