namespace Haruspex.Ir;

/// <summary>A value whose type is not known; the document's unknowns say why.</summary>
/// <param name="Reason">One of <see cref="IrUnknownReasons"/>.</param>
public sealed record IrUnknownType(string Reason) : IrType;
