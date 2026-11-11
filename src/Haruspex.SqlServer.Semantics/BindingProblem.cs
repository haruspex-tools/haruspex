using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Semantics;

/// <summary>Why a reference cannot be bound.</summary>
/// <param name="Kind">The kind of problem.</param>
/// <param name="Detail">A sentence saying what was found.</param>
/// <param name="At">Where.</param>
public sealed record BindingProblem(BindingProblemKind Kind, string Detail, SourceLocation At);
