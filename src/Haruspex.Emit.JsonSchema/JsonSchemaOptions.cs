namespace Haruspex.Emit.JsonSchema;

/// <summary>
/// Settings for the JSON Schema emitter. The emitter never reads the config file: whoever composes the tool (the CLI) maps the
/// config's settings to these.
/// </summary>
public sealed record JsonSchemaOptions
{
    /// <summary>How result objects treat keys their type does not list.</summary>
    public JsonSchemaObjects Objects { get; init; } = JsonSchemaObjects.Strip;

    /// <summary>
    /// For a document split into modules, the name the shared document is referred to by, relative to each module's document:
    /// <c>shared.jsonschema.json</c>, the file the CLI writes beside them. Unused by a document of one module.
    /// </summary>
    public string SharedDocument { get; init; } = "shared.jsonschema.json";
}
