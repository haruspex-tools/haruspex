using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Semantics;

/// <summary>Exactly one schema has an object of the name, so a statement that runs at all reads that one.</summary>
/// <param name="Named">The object, of the kinds the name was looked for among.</param>
public sealed record OneObject(SchemaObject Named) : SchemaNameResolution;
