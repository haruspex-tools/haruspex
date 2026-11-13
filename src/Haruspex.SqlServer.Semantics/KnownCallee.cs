using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Semantics;

/// <summary>The call names this function of the project, which may still not be a table-valued one.</summary>
/// <param name="Function">The function.</param>
public sealed record KnownCallee(Function Function) : Callee;
