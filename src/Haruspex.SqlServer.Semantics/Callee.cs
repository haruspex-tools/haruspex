namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// The function a call of a table-valued function in FROM names: one of the project, or why none. An unqualified name means
/// the one function of that name when exactly one schema has it, as a table's does (SQL Server looks in the caller's default
/// schema, then in <c>dbo</c>).
/// </summary>
/// <remarks>A closed hierarchy: <see cref="KnownCallee"/>, <see cref="AmbiguousCallee"/>, <see cref="MissingCallee"/>, <see cref="ForeignCallee"/>, and <see cref="XmlNodesCallee"/>, a call that names no function.</remarks>
public abstract record Callee
{
    private protected Callee()
    {
    }
}
