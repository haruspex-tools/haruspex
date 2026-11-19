namespace Haruspex.Ir;

/// <summary>A field, the named type it is a field of, and the path a person names it by (<see cref="IrFieldPlaces"/>).</summary>
/// <param name="TypeName">The name of the object type the field belongs to.</param>
/// <param name="Field">The field.</param>
/// <param name="Path">Its path in its object's results, as <c>haruspex explain</c> takes it: <c>lines[].qty</c>, <c>#2.total</c>.</param>
public readonly record struct IrFieldPlace(string TypeName, IrField Field, string Path);
