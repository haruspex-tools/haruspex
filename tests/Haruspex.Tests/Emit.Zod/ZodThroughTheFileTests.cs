using Haruspex.Emit.Zod;
using Haruspex.Ir;

namespace Haruspex.Tests.Emit.Zod;

/// <summary>
/// The Zod emitter fed what a consumer of the IR file gets: every committed IR snapshot, read with <see cref="IrJsonReader"/>,
/// must emit exactly the committed Zod beside it. So the emitter is tested from the file, not from records that only
/// inference builds, and an emitter written against the file (in another language, say) has the same cases to pass.
/// </summary>
/// <remarks>
/// The snapshot tests (<c>FixtureSnapshotTests</c>) check the other half: that inference writes the committed IR. Together
/// they say that the published file alone is enough to generate the published Zod.
/// </remarks>
public class ZodThroughTheFileTests
{
    public static TheoryData<string> Snapshots { get; } =
        [.. Fixture.All.SelectMany(f => f.Snapshots).Select(s => Path.GetRelativePath(RepositoryPaths.Fixtures, s.Ir))];

    [Theory]
    [MemberData(nameof(Snapshots))]
    public void The_committed_ir_file_emits_the_committed_zod(string snapshot)
    {
        var fixture = Fixture.Named(snapshot.Split(Path.DirectorySeparatorChar)[0]);
        var (ir, zod) = fixture.Snapshots.Single(s => Path.GetRelativePath(RepositoryPaths.Fixtures, s.Ir) == snapshot);

        var emitted = ZodEmitter.Emit(IrJsonReader.Read(File.ReadAllText(ir)), Options(fixture));

        Assert.Equal(File.ReadAllText(zod), emitted);
    }

    [Fact]
    public void Every_committed_ir_file_has_its_zod_beside_it() =>
        Assert.All(Fixture.All.SelectMany(f => f.Snapshots), s => Assert.True(File.Exists(s.Zod), $"{s.Ir} has no {Path.GetFileName(s.Zod)} beside it."));

    /// <summary>The emitter's settings as the CLI takes them from a project's config file; the defaults otherwise.</summary>
    private static ZodOptions Options(Fixture fixture) =>
        fixture.IsProject && ConfigReader.Read(fixture.Config!).Config is { Objects: ObjectsPolicy.Strict }
            ? new ZodOptions { Objects = ZodObjects.Strict }
            : new ZodOptions();
}
