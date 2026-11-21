using Haruspex.Collections;
using Haruspex.Ir;

namespace Haruspex.SqlServer.Inference;

/// <summary>An object of these keys, before naming (<see cref="IrObjectType"/>).</summary>
internal sealed record ObjectShape(EquatableArray<FieldShape> Fields) : Shape
{
    /// <summary>
    /// Whether its values may hold keys it doesn't list, of any value (<see cref="IrObjectType.Open"/>): JSON a column is
    /// declared to hold, whose writer may have added others. An object the database builds holds only its own keys.
    /// </summary>
    public bool Open { get; init; }
}
