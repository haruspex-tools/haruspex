namespace Haruspex.Config;

/// <summary>What <c>haruspex generate</c> writes beside the IR (<c>"emit"</c>), each from the IR alone.</summary>
public enum Emitter
{
    /// <summary>A TypeScript module of Zod schemas, <c>haruspex.zod.ts</c>. The default.</summary>
    Zod,

    /// <summary>A JSON Schema document, <c>haruspex.jsonschema.json</c>.</summary>
    JsonSchema,
}
