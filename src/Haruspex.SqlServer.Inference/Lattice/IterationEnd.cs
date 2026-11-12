namespace Haruspex.SqlServer.Inference.Lattice;

/// <summary>How an iteration ended.</summary>
internal enum IterationEnd
{
    /// <summary>A pass gave back what it started from: the state is the least fixed point.</summary>
    Settled,

    /// <summary>The step stopped it.</summary>
    Stopped,

    /// <summary><see cref="FixedPoint.MostPasses"/> passes were taken without settling.</summary>
    Unsettled,
}
