namespace Haruspex.Ir;

/// <summary>A type directly inside another, and the step of an IR path that leads to it (<see cref="IrTypeTree.Children"/>).</summary>
/// <remarks>A value a walk yields, not part of the format: nothing in the IR file is one.</remarks>
/// <param name="Type">The inner type.</param>
/// <param name="Segment">
/// The path segment from the outer type to it: <c>[]</c> for an array's items, <c>[i]</c> for a tuple's element, <c>{}</c>
/// for a record's values and <c>&lt;json&gt;</c> for what JSON text holds. Null for a union's variant, which shares its
/// union's path, as its unknowns do.
/// </param>
public readonly record struct IrTypeChild(IrType Type, IrPathSegment? Segment);
