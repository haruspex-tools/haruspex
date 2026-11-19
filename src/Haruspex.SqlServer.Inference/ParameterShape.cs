using Haruspex.Ir;

namespace Haruspex.SqlServer.Inference;

/// <summary>A parameter as a caller sees it: its name, the shape of what may be sent, and whether it may be left out or comes back.</summary>
/// <param name="Name">Its name, with its <c>@</c>.</param>
/// <param name="Type">What a caller may send.</param>
/// <param name="HasDefault">Whether a caller may leave it out.</param>
/// <param name="IsOutput">Whether its value comes back.</param>
internal sealed record ParameterShape(string Name, Shape Type, bool HasDefault, bool IsOutput)
{
    /// <summary>For a table-valued parameter of a table type the model defines, the type's name and definition; null otherwise.</summary>
    public (string Key, IrTableType Definition)? TableType { get; init; }
}
