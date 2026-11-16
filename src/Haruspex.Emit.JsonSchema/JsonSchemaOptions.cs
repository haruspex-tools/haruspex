namespace Haruspex.Emit.JsonSchema;

/// <summary>
/// Settings for the JSON Schema emitter. The emitter never reads the config file: whoever composes the tool (the CLI) maps the
/// config's settings to these.
/// </summary>
public sealed record JsonSchemaOptions
{
    /// <summary>How result objects treat keys their type does not list.</summary>
    public JsonSchemaObjects Objects { get; init; } = JsonSchemaObjects.Strip;
}
