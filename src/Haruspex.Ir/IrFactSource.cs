namespace Haruspex.Ir;

/// <summary>Where a fact came from, for the origins graph.</summary>
/// <param name="Kind">One of <see cref="IrFactSourceKind"/>.</param>
/// <param name="Ref">The type's name for a type, or the constraint's name (or its location, when unnamed) for a check.</param>
public sealed record IrFactSource(IrFactSourceKind Kind, string Ref);
