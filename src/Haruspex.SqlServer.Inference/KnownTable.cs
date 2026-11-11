using Haruspex.Ir;

namespace Haruspex.SqlServer.Inference;

/// <summary>The procedure created the table, so its columns and what each may hold are known.</summary>
/// <param name="Name">The table's name, <c>#t</c>.</param>
/// <param name="Columns">Its columns, in order.</param>
internal sealed record KnownTable(string Name, EquatableArray<TempColumn> Columns) : TempTable;
