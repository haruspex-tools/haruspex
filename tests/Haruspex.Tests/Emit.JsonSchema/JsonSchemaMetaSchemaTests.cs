using Haruspex.Emit.JsonSchema;

namespace Haruspex.Tests.Emit.JsonSchema;

/// <summary>
/// Every JSON Schema document the emitter gives is a valid draft 2020-12 schema: checked against the meta-schema with Ajv, pinned in
/// <c>tests/emit-check</c>, and compiled in Ajv's strict mode, which also refuses a keyword it doesn't know and a pattern that isn't
/// a regular expression with the <c>u</c> flag. <c>src/meta-schema.mjs</c> there runs the check.
/// </summary>
public class JsonSchemaMetaSchemaTests
{
    [Fact]
    public void Every_emitted_document_is_a_valid_2020_12_schema_that_compiles_strictly()
    {
        EmitCheckPackage.RequireNode(RepositoryPaths.EmitCheck, "the generated JSON Schema was not checked against the meta-schema");
        using var folder = new TempDirectory();

        // Each fixture's whole document, both ways objects can be, and every committed snapshot, which the snapshot tests hold to
        // what the emitter gives one procedure.
        var documents = Fixture.All.SelectMany(f => new[]
            {
                folder.File($"{f.Name}.strip.jsonschema.json", JsonSchemaEmitter.Emit(f.Ir).Expected()),
                folder.File($"{f.Name}.strict.jsonschema.json", JsonSchemaEmitter.Emit(f.Ir, new JsonSchemaOptions { Objects = JsonSchemaObjects.Strict }).Expected()),
            })
            .Concat(Fixture.All.SelectMany(f => f.JsonSchemaSnapshots).Select(s => s.JsonSchema))

            // A split project's documents, each a module's; that their references resolve together is JsonSchemaModulesTests'.
            .Concat(Fixture.All.Where(f => f.IsSplit).SelectMany(f => Directory.GetFiles(f.Expected, "*.jsonschema.verified.json")).Order(StringComparer.Ordinal))
            .ToArray();

        EmitCheckPackage.EnsureInstalled(RepositoryPaths.EmitCheck);
        Assert.True(
            EmitCheckPackage.TryRun("node", [Path.Combine("src", "meta-schema.mjs"), .. documents], RepositoryPaths.EmitCheck, out var output, out var why),
            $"A generated JSON Schema is not a valid 2020-12 schema ({why}):\n{output}");
    }
}
