namespace Haruspex.SqlServer.Inference.Lattice;

/// <summary>How an iteration ended, and the state it ended with.</summary>
/// <param name="End">Settled at a fixed point, stopped by the step, or given up at the cap.</param>
/// <param name="Last">The fixed point when settled; otherwise the last state reached.</param>
internal readonly record struct Iteration<T>(IterationEnd End, T Last);
