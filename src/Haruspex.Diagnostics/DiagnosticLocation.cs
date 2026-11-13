namespace Haruspex.Diagnostics;

/// <summary>
/// Where a problem is: a place in a SQL file (<see cref="SourceLocation"/>), a value in a JSON document such as a config
/// file (<see cref="JsonLocation"/>), or a file or folder as a whole (<see cref="FileLocation"/>).
/// </summary>
/// <remarks>
/// A closed hierarchy (docs/decisions/0031-the-model-is-closed-hierarchies-of-records.md), so the formatter's switch over the
/// kinds is checked for each one, and a kind added later fails the build until every reader handles it.
/// </remarks>
public abstract record DiagnosticLocation
{
    private protected DiagnosticLocation()
    {
    }
}
