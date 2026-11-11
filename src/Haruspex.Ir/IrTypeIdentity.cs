namespace Haruspex.Ir;

/// <summary>Where an object type comes from: the database object it belongs to, and the path to it there.</summary>
/// <param name="Owner">What kind of object it belongs to.</param>
/// <param name="Object">The object's name in the database, such as <c>sales.get_order</c> or <c>bom.part_tree</c>.</param>
/// <param name="Path">
/// The path to the type in the IR's path syntax (<see cref="IrPath"/>): <c>results[0][]</c> for a procedure's rows,
/// <c>&lt;bom.part_tree&gt;</c> for a recursive function's node and <c>&lt;bom.part_tree&gt;.part</c> inside it. The
/// variants of a union share their path, as their unknowns do, so two types may have the same identity.
/// </param>
public sealed record IrTypeIdentity(IrTypeOwner Owner, string Object, string Path);
