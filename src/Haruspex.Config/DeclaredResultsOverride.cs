using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;

namespace Haruspex.Config;

/// <summary>
/// A procedure's results declared (<c>results</c>, with its <c>why</c>), for results the project can't show. A procedure need
/// not be selected, or in the project, to have them: a call of it contributes them to its caller.
/// </summary>
public sealed record DeclaredResultsOverride : ProcedureOverride
{
    /// <summary>A declaration of a procedure's results.</summary>
    /// <param name="procedure">The procedure.</param>
    /// <param name="key">The key as written.</param>
    /// <param name="name">The generated name to use, or null.</param>
    /// <param name="results">The declared results.</param>
    /// <param name="at">Where its key is written.</param>
    /// <param name="nameAt">Where its name is written, when it has one.</param>
    public DeclaredResultsOverride(ObjectName procedure, string key, string? name, ResultsDeclaration results, JsonLocation at, JsonLocation? nameAt)
        : base(procedure, key, name, at, nameAt)
    {
        Results = results;
    }

    /// <summary>The declared results.</summary>
    public ResultsDeclaration Results { get; }
}
