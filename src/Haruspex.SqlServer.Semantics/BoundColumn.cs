using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Semantics;

/// <summary>A column reference bound to a column of a table in scope.</summary>
/// <param name="Source">The source it was found in.</param>
/// <param name="Table">The source's table.</param>
/// <param name="Column">The column.</param>
public sealed record BoundColumn(BoundSource Source, Table Table, Column Column) : ColumnBinding;
