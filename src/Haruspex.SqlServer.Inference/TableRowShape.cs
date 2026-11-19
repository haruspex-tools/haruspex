using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// A row of a table type, as a client sends one in a table-valued parameter: a reference to a named type that the IR's type
/// table defines once for every parameter of the type (<see cref="IrBuilder"/>).
/// </summary>
/// <param name="Type">The table type.</param>
/// <param name="Row">The row's columns, in the type's order.</param>
internal sealed record TableRowShape(ObjectName Type, ObjectShape Row) : Shape;
