namespace Haruspex.Ir;

/// <summary>
/// An array of a fixed length whose every position has its own type, as <c>json_array</c> builds one: each element is
/// there, possibly null under <c>NULL ON NULL</c>.
/// </summary>
public sealed record IrTuple(IReadOnlyList<IrElement> Items) : IrType;
