using Haruspex.Ir;
using Haruspex.SqlServer.Semantics;

namespace Haruspex.SqlServer.Inference;

/// <summary>A relation's unique key: columns whose values no two of its rows share, at most one row holding NULL in all.</summary>
/// <param name="Ordinals">The key's columns, by position in the relation.</param>
/// <param name="Types">Each column's built-in SQL type, for comparing it with a value in its own type (Cardinality.cs).</param>
internal sealed record RelationKey(EquatableArray<int> Ordinals, EquatableArray<BuiltInType> Types);
