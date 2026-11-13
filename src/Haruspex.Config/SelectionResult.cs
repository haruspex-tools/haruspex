using Haruspex.Collections;
using Haruspex.SqlServer.Model;

namespace Haruspex.Config;

/// <summary>The procedures selected, and every problem with the patterns that selected them.</summary>
/// <param name="Procedures">The selected procedures, in the order given.</param>
/// <param name="Problems">Patterns and schemas that match nothing; selection fails when there are any.</param>
public sealed record SelectionResult(EquatableArray<ObjectName> Procedures, EquatableArray<ConfigProblem> Problems);
