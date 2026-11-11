namespace Haruspex.Emit.Zod;

/// <summary>
/// Settings for the Zod emitter. The emitter never reads the config file: whoever composes the tool (the CLI) maps the
/// config's settings to these.
/// </summary>
public sealed record ZodOptions
{
    /// <summary>How result objects treat keys their type does not list.</summary>
    public ZodObjects Objects { get; init; } = ZodObjects.Strip;
}
