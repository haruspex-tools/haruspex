using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Semantics;

/// <summary>The call names a function without a schema, and more than one schema has one of that name: which runs depends on the caller.</summary>
/// <param name="Name">The name as written, without a schema.</param>
/// <param name="Candidates">The functions of that name, in order of their full names.</param>
public sealed record AmbiguousCallee(string Name, IReadOnlyList<Function> Candidates) : Callee;
