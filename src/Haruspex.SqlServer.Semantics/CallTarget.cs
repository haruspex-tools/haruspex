using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Semantics;

/// <summary>What an <c>EXEC</c> runs: a procedure of the project, or something whose results the project cannot show.</summary>
public abstract record CallTarget
{
    /// <summary>Only this assembly derives a case, so the set is closed (docs/decisions/0031-the-model-is-closed-hierarchies-of-records.md).</summary>
    /// <param name="at">Where the <c>EXEC</c> is.</param>
    private protected CallTarget(SourceLocation at)
    {
        At = at;
    }

    /// <summary>Where the <c>EXEC</c> is.</summary>
    public SourceLocation At { get; }
}
