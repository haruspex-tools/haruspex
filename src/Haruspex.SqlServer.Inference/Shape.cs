namespace Haruspex.SqlServer.Inference;

/// <summary>
/// A shape as inference works with it: the IR's type before naming. Objects are anonymous here, and unknowns carry their
/// own location; <see cref="IrBuilder"/> turns shapes into the IR.
/// </summary>
internal abstract record Shape
{
    /// <summary>Only this assembly derives a case, so the set is closed (docs/decisions/0031-the-model-is-closed-hierarchies-of-records.md).</summary>
    private protected Shape()
    {
    }
}
