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
        // what the emitter gives one procedure; and a split project's documents, each a module's (that their references resolve
        // together is JsonSchemaModulesTests'). All are written to one folder, and node is given the folder, not each file: the
        // command line stays short however many fixtures there are (EmitCheckPackage.TryRun). A copy is named after its fixture,
        // so two fixtures' snapshots of the same name don't meet. The script reads the folder itself, so the count it prints is
        // checked against the count written: a check that read nothing would pass.
        var written = 0;
        foreach (var f in Fixture.All)
        {
            folder.File($"{f.Name}.strip.jsonschema.json", JsonSchemaEmitter.Emit(f.Ir).Expected());
            folder.File($"{f.Name}.strict.jsonschema.json", JsonSchemaEmitter.Emit(f.Ir, new JsonSchemaOptions { Objects = JsonSchemaObjects.Strict }).Expected());
            written += 2;
            var committed = f.JsonSchemaSnapshots.Select(s => s.JsonSchema)
                .Concat(f.IsSplit ? Directory.GetFiles(f.Expected, "*.jsonschema.verified.json") : []);
            foreach (var path in committed)
            {
                File.Copy(path, Path.Combine(folder.Path, $"{f.Name}.{Path.GetFileName(path)}"));
                written++;
            }
        }

        EmitCheckPackage.EnsureInstalled(RepositoryPaths.EmitCheck);
        Assert.True(
            EmitCheckPackage.TryRun("node", [Path.Combine("src", "meta-schema.mjs"), folder.Path], RepositoryPaths.EmitCheck, out var output, out var why),
            $"A generated JSON Schema is not a valid 2020-12 schema ({why}):\n{output}");
        Assert.Contains($"{written} document(s), 0 failed", output, StringComparison.Ordinal);
    }
}
