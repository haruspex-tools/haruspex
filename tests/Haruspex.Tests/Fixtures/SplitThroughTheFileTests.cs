using Haruspex.Cli;
using Haruspex.Ir;
using Haruspex.SqlServer.Sources;

namespace Haruspex.Tests.Fixtures;

/// <summary>
/// The emitters fed a split project's committed IR file, as <c>ZodThroughTheFileTests</c> feeds them every other one: read back,
/// and split into modules as its config file says, it must give exactly the files committed beside it. The IR records no module,
/// so the config file and the query files' comments are read again, which is what a consumer splitting the file would do
/// (docs/decisions/0074-a-config-may-split-its-output-into-modules.md).
/// </summary>
public class SplitThroughTheFileTests
{
    public static TheoryData<string> Projects { get; } = [.. Fixture.All.Where(f => f.IsSplit).Select(f => f.Name)];

    [Theory]
    [MemberData(nameof(Projects))]
    public void The_committed_ir_file_split_as_the_config_says_emits_each_committed_file(string name)
    {
        var fixture = Fixture.Named(name);
        var document = IrJsonReader.Read(File.ReadAllText(Path.Combine(fixture.Expected, "haruspex.ir.verified.json")));
        var config = ConfigReader.Read(fixture.Config!).Expected();
        var queries = QuerySelection.Select(config).Expected().Select(f => QueryFileReader.Read(f.Path, f.Relative).Expected()).ToList();
        var ids = document.Objects.Select(o => o.Id).ToHashSet(StringComparer.Ordinal);
        var assigned = ModuleAssignment.Assign(config, fixture.Project.Procedures.Where(p => ids.Contains(p.ToString())), queries).Expected();

        var files = SplitModules.Emit(config, document, IrModules.Of(document, assigned)).Expected();

        Assert.All(files, file =>
        {
            var snapshot = Path.Combine(fixture.Expected, Path.GetFileNameWithoutExtension(file.Name) + ".verified" + Path.GetExtension(file.Name));
            Assert.Equal(File.ReadAllText(snapshot), file.Text);
        });
        Assert.Equal(
            Directory.GetFiles(fixture.Expected).Select(Path.GetFileName).Where(f => f != "haruspex.ir.verified.json").Order(StringComparer.Ordinal),
            files.Select(f => Path.GetFileNameWithoutExtension(f.Name) + ".verified" + Path.GetExtension(f.Name)).Order(StringComparer.Ordinal));
    }
}
