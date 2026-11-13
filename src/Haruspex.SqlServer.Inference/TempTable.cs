namespace Haruspex.SqlServer.Inference;

// What the statement walk (Walker.cs) carries from one statement to the next, and how two states join
// where paths meet. README.md in this folder gives the model and why each join is sound.

/// <summary>What is known of one temporary table at a point in the procedure.</summary>
internal abstract record TempTable
{
    /// <summary>Only this assembly derives a case, so the set is closed (docs/decisions/0031-the-model-is-closed-hierarchies-of-records.md).</summary>
    private protected TempTable()
    {
    }
}
