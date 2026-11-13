using Haruspex.SqlServer.Semantics;

namespace Haruspex.SqlServer.Inference;

/// <summary>A variable that holds JSON built here, by its name.</summary>
/// <param name="Variable">The variable, as declared.</param>
internal sealed record VariableSlot(LocalName Variable) : JsonSlotKey
{
    /// <summary>The variable's name, as messages give it: <c>@payload</c>.</summary>
    public override string ToString() => Variable.Text;
}
