using System.Text.Json;

namespace Haruspex.Tests.Schemas;

/// <summary>
/// What Ajv, the JSON Schema validator a consumer is most likely to use, makes of each of a set of documents against one of
/// haruspex's own schemas, compiled in strict mode (<c>tests/emit-check/src/verdicts.mjs</c>). <see cref="SchemaSubset"/> is
/// written for the tests; a keyword it read differently from a real validator would pass both a schema and its tests, so the tests
/// that hold the IR's schema to its reader ask Ajv too.
/// </summary>
internal static class AjvVerdicts
{
    /// <summary>
    /// Each document's errors under <paramref name="schema"/>, by its name, empty when it is valid. Skips the calling test when Node.js
    /// can't be run (<see cref="EmitCheckPackage.RequireNode"/>), and fails it when the schema doesn't compile in strict mode.
    /// </summary>
    /// <param name="schema">The schema file's path.</param>
    /// <param name="documents">Each document's text, by a name unique among them.</param>
    public static Dictionary<string, List<string>> Of(string schema, IReadOnlyDictionary<string, string> documents)
    {
        EmitCheckPackage.RequireNode(RepositoryPaths.EmitCheck, $"{Path.GetFileName(schema)} was not checked with Ajv");
        EmitCheckPackage.EnsureInstalled(RepositoryPaths.EmitCheck);

        // The folder, not each file: the command line stays short however many documents there are (EmitCheckPackage.TryRun).
        using var folder = new TempDirectory();
        var names = documents.Keys.Select((name, i) => (File: $"{i:D4}.json", Name: name)).ToDictionary(p => p.File, p => p.Name, StringComparer.Ordinal);
        foreach (var (file, name) in names)
            folder.File(file, documents[name]);

        Assert.True(
            EmitCheckPackage.TryRun("node", [Path.Combine("src", "verdicts.mjs"), schema, folder.Path], RepositoryPaths.EmitCheck, out var output, out var why),
            $"{schema} doesn't compile in Ajv's strict mode ({why}):\n{output}");
        var verdicts = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(output) ?? [];

        // Every document written was read: a check that read nothing would pass.
        Assert.Equal(names.Keys.Order(StringComparer.Ordinal), verdicts.Keys.Order(StringComparer.Ordinal));
        return verdicts.ToDictionary(v => names[v.Key], v => v.Value, StringComparer.Ordinal);
    }
}
