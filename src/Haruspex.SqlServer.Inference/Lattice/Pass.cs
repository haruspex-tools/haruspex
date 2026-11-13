namespace Haruspex.SqlServer.Inference.Lattice;

/// <summary>One pass of an iteration: the next state, or a stop the solver handles itself (a body that can't be walked).</summary>
internal readonly record struct Pass<T>(T Next, bool Stopped)
{
    public static Pass<T> To(T next) => new(next, false);

    // Never null: as read, since FixedPoint.Iterate reads Next only of a pass that is not stopped, which has no next state.
    public static Pass<T> Stop() => new(default!, true);
}
