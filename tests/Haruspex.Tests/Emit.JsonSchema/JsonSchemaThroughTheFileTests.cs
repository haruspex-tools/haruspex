using Haruspex.Diagnostics;
using Haruspex.Emit.JsonSchema;
using Haruspex.Ir;

namespace Haruspex.Tests.Emit.JsonSchema;

/// <summary>
/// The JSON Schema emitter fed what a consumer of the IR file gets, as <c>ZodThroughTheFileTests</c> feeds the Zod emitter:
/// every committed IR snapshot, read with <see cref="IrJsonReader"/>, must emit exactly the committed JSON Schema beside it, so
/// the published file alone is enough to generate the published schema.
/// </summary>
public class JsonSchemaThroughTheFileTests
{
    public static TheoryData<string> Snapshots { get; } =
        [.. Fixture.All.SelectMany(f => f.JsonSchemaSnapshots).Select(s => Path.GetRelativePath(RepositoryPaths.Fixtures, s.Ir))];

    [Theory]
    [MemberData(nameof(Snapshots))]
    public void The_committed_ir_file_emits_the_committed_json_schema(string snapshot)
    {
        var fixture = Fixture.Named(snapshot.Split(Path.DirectorySeparatorChar)[0]);
        var (ir, schema) = fixture.JsonSchemaSnapshots.Single(s => Path.GetRelativePath(RepositoryPaths.Fixtures, s.Ir) == snapshot);

        var emitted = JsonSchemaEmitter.Emit(IrJsonReader.Read(File.ReadAllText(ir)), Options(fixture)).Expected();

        Assert.Equal(File.ReadAllText(schema), emitted);
    }

    [Fact]
    public void Every_committed_ir_file_has_its_json_schema_beside_it() =>
        Assert.All(Fixture.All.SelectMany(f => f.JsonSchemaSnapshots), s => Assert.True(File.Exists(s.JsonSchema), $"{s.Ir} has no {Path.GetFileName(s.JsonSchema)} beside it."));

    /// <summary>The emitter's settings as the CLI takes them from a project's config file; the defaults otherwise.</summary>
    private static JsonSchemaOptions Options(Fixture fixture) =>
        fixture.IsProject && ConfigReader.Read(fixture.Config!) is Succeeded<HaruspexConfig> { Value.Objects: ObjectsPolicy.Strict }
            ? new JsonSchemaOptions { Objects = JsonSchemaObjects.Strict }
            : new JsonSchemaOptions();
}
