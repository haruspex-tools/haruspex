namespace Haruspex.SqlServer.Semantics;

/// <summary>A variable the body neither declares nor takes as a parameter, which SQL Server refuses (137).</summary>
/// <param name="Name">The name as written.</param>
public sealed record UndeclaredVariable(LocalName Name) : VariableResolution
{
    /// <inheritdoc/>
    public override LocalName Name { get; } = Name;
}
