namespace Haruspex.Ir;

/// <summary>
/// Of a field of a document combining two databases (0.25), the field each gives, so that <c>haruspex explain</c> can show each
/// database's derivation and how the two were combined. A side is null where that database doesn't return the key.
/// </summary>
/// <param name="Baseline">The field as the baseline gives it, or null when it doesn't return the key.</param>
/// <param name="Candidate">The field as the document's own source gives it, or null when it doesn't return the key.</param>
public sealed record IrFieldSides(IrFieldSide? Baseline, IrFieldSide? Candidate);
