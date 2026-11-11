namespace Haruspex.Ir;

/// <summary>
/// The start of a path inside a type that several objects share, named by the database object it comes from:
/// <c>&lt;bom.part_tree&gt;</c> for the node of a recursive function.
/// </summary>
public sealed record IrPathShared(string Owner) : IrPathSegment;
