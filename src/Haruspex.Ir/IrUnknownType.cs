namespace Haruspex.Ir;

/// <summary>A value whose type is not known; the document's unknowns say why.</summary>
/// <param name="Reason">One of <see cref="IrUnknownReason"/>.</param>
public sealed record IrUnknownType(IrUnknownReason Reason) : IrType;
