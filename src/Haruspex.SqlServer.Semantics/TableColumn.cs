using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Semantics;

/// <summary>A column of a table of the schema, read through a source in scope.</summary>
/// <param name="Source">The source.</param>
/// <param name="Table">The source's table.</param>
/// <param name="Column">The column.</param>
public sealed record TableColumn(BoundSource Source, Table Table, Column Column) : Resolution;
