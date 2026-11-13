using Haruspex.Collections;

namespace Haruspex.Ir;

/// <summary>
/// A value that has one of several types, such as an item of a <c>json_array</c> whose elements differ in type. At
/// least two variants, none of them the same.
/// </summary>
public sealed record IrUnion(EquatableArray<IrType> Variants) : IrType;
