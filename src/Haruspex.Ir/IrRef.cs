namespace Haruspex.Ir;

/// <summary>A reference to a named object type in <see cref="IrDocument.Types"/>.</summary>
public sealed record IrRef(string Name) : IrType;
