namespace Haruspex.SqlServer.Semantics;

/// <summary>How a source comes to be on an optional side (<see cref="OptionalSide"/>).</summary>
public enum OptionalSideKind
{
    /// <summary>An outer join's optional side: a <c>LEFT JOIN</c>'s right, either side of a <c>FULL JOIN</c>.</summary>
    OuterJoin,

    /// <summary>An <c>OUTER APPLY</c>'s right side, which the source is the whole of.</summary>
    WholeApplied,

    /// <summary>An <c>OUTER APPLY</c>'s right side, which the source is joined inside.</summary>
    InsideApplied,
}
