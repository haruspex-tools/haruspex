namespace Haruspex.SqlServer.Semantics;

/// <summary>Which end of a number's values a <see cref="Bound"/> limits.</summary>
public enum BoundSide
{
    /// <summary>The least value: a bound that rises allows fewer values.</summary>
    Lower,

    /// <summary>The greatest value: a bound that falls allows fewer values.</summary>
    Upper,
}
