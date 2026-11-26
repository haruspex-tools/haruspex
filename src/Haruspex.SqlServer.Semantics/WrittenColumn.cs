using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Semantics;

/// <summary>A column of a CTE or derived table written through (<see cref="WrittenThrough"/>): the name it gives a table's column, unchanged.</summary>
/// <param name="Name">The source's name for it.</param>
/// <param name="Column">The table's column.</param>
public sealed record WrittenColumn(string Name, Column Column);
