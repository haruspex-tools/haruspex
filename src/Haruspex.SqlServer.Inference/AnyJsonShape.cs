using Haruspex.Ir;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// Any JSON value, of any content (<see cref="IrAnyJson"/>): what a column declared with the schema <c>{}</c> holds,
/// and, narrowed to an object or an array, what <c>json_query</c> gives of it.
/// </summary>
/// <param name="ObjectOrArray">Whether it is only ever an object or an array.</param>
internal sealed record AnyJsonShape(bool ObjectOrArray = false) : Shape;
