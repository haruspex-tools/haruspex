namespace Haruspex.SqlServer.Inference.Lattice;

/// <summary>How an iteration ended.</summary>
internal enum IterationEnd
{
    /// <summary>
    /// A pass gave back what it started from: the state is a fixed point, the least one unless a widening applied, which
    /// leaves a sound one above it.
    /// </summary>
    Settled,

    /// <summary>The step stopped it.</summary>
    Stopped,

    /// <summary><see cref="FixedPoint.MostPasses"/> passes were taken without settling.</summary>
    Unsettled,
}
