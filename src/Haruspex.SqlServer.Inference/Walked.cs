using Haruspex.Collections;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Inference;

/// <summary>What walking a procedure's body found.</summary>
/// <param name="End">The state at its end, joined over the paths that return successfully; null when none does.</param>
/// <param name="Escapes">The states an error may leave it from, when asked for (<see cref="CallContext.Escapes"/>).</param>
/// <param name="Blocker">The statement that makes its results impossible to list, if any; then nothing else is known.</param>
/// <param name="Declared">The procedures whose declared results it took, at any depth of calls.</param>
internal sealed record Walked(FlowState? End, EquatableArray<FlowState> Escapes, UnknownShape? Blocker, EquatableArray<ObjectName> Declared)
{
    /// <summary>
    /// The procedures of <paramref name="declared"/> and <paramref name="more"/>, each once, in the order of their names as
    /// written (ordinal), so that a document lists the claims they rest on in a stable order.
    /// </summary>
    public static EquatableArray<ObjectName> Union(EquatableArray<ObjectName> declared, IEnumerable<ObjectName> more) =>
        [.. declared.Concat(more).Distinct().OrderBy(name => name.ToString(), StringComparer.Ordinal)];
}
