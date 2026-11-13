using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Semantics;

/// <summary>A parameter of the procedure or function whose body it is.</summary>
/// <param name="Parameter">The parameter.</param>
public sealed record ParameterVariable(Parameter Parameter) : VariableResolution
{
    /// <inheritdoc/>
    public override string Name => Parameter.Name;
}
