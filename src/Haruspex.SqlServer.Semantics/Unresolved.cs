namespace Haruspex.SqlServer.Semantics;

/// <summary>A column reference that can't be bound for certain, and why.</summary>
public abstract record Unresolved : Resolution
{
    private protected Unresolved(BindingProblem problem)
    {
        Problem = problem ?? throw new ArgumentNullException(nameof(problem));
    }

    /// <summary>Why, located at the reference.</summary>
    public BindingProblem Problem { get; }
}
