using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// That a source is on the optional side of an outer join or an <c>OUTER APPLY</c>, which has no row where nothing matches.
/// This is structure, not nullability; what it implies for values is the reader's to decide.
/// </summary>
/// <param name="At">Where that side is, in the outermost such join: an outer join enclosing an <c>APPLY</c> makes its side the join's.</param>
/// <param name="Kind">Whether it is an outer join's side or an <c>OUTER APPLY</c>'s, and for the latter whether the source is the side whole.</param>
public sealed record OptionalSide(SourceLocation At, OptionalSideKind Kind)
{
    /// <summary>Whether it is an <c>OUTER APPLY</c>'s right side rather than an outer join's.</summary>
    public bool IsApplied => Kind != OptionalSideKind.OuterJoin;
}
