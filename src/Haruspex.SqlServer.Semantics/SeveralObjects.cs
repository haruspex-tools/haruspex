using Haruspex.Collections;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Semantics;

/// <summary>More than one schema has an object of the name: which a statement reads depends on the caller's default schema.</summary>
/// <param name="Candidates">Each, in the order of their full names and then their kinds, for a message to list.</param>
public sealed record SeveralObjects(EquatableArray<SchemaObject> Candidates) : SchemaNameResolution;
