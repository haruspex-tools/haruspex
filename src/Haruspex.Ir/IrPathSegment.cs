namespace Haruspex.Ir;

/// <summary>One step of a path into an object's results (see <see cref="IrPath"/>).</summary>
public abstract record IrPathSegment
{
    /// <summary>Only this assembly derives a case, so the set is closed (docs/decisions/0031-the-model-is-closed-hierarchies-of-records.md).</summary>
    private protected IrPathSegment()
    {
    }
}
