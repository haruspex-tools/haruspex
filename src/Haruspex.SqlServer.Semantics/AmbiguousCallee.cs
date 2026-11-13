using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Semantics;

/// <summary>The call names a function without a schema, and more than one schema has one of that name: which runs depends on the caller.</summary>
/// <param name="Candidates">The functions of that name, in order of their full names.</param>
public sealed record AmbiguousCallee(IReadOnlyList<Function> Candidates) : Callee;
