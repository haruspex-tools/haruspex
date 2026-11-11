namespace Haruspex.SqlServer.Inference;

/// <summary>Nothing is known of the table's shape, for the reason given, which every column read from it reports.</summary>
internal sealed record UnknownTable(UnknownShape Why) : TempTable;
