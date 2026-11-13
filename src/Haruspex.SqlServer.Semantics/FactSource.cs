namespace Haruspex.SqlServer.Semantics;

/// <summary>Where a fact came from.</summary>
public abstract record FactSource
{
    /// <summary>Only this assembly derives a case, so the set is closed (docs/decisions/0031-the-model-is-closed-hierarchies-of-records.md).</summary>
    private protected FactSource()
    {
    }
}
