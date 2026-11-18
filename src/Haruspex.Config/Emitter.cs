namespace Haruspex.Config;

/// <summary>What <c>haruspex generate</c> writes beside the IR (<c>"emit"</c>), each from the IR alone or, for the mapping helper, from nothing.</summary>
public enum Emitter
{
    /// <summary>A TypeScript module of Zod schemas, <c>haruspex.zod.ts</c>. The default.</summary>
    Zod,

    /// <summary>A JSON Schema document, <c>haruspex.jsonschema.json</c>.</summary>
    JsonSchema,

    /// <summary>
    /// The database module's functions over the mssql driver, <c>haruspex.mssql.ts</c>, which import the Zod module's schemas,
    /// so it needs <see cref="Zod"/> beside it.
    /// </summary>
    Mssql,

    /// <summary>
    /// The mapping helper, <c>haruspex.mapping.ts</c>: TypeScript types with which an application's mapper accounts for every key
    /// of a generated row. It is the same for every module and made from nothing in the IR; it works on the types the Zod module
    /// declares, so it needs <see cref="Zod"/> beside it (<c>docs/decisions/0066-a-mapper-accounts-for-every-key-of-a-generated-row.md</c>).
    /// </summary>
    Mapping,
}
