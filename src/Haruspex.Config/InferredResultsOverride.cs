using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;

namespace Haruspex.Config;

/// <summary>Overrides of a procedure's inferred results: its name, and places inside its results (<c>paths</c>).</summary>
public sealed record InferredResultsOverride : ProcedureOverride
{
    /// <summary>Overrides of a procedure whose results haruspex infers.</summary>
    /// <param name="procedure">The procedure.</param>
    /// <param name="key">The key as written.</param>
    /// <param name="name">The generated name to use, or null.</param>
    /// <param name="paths">Overrides of places inside its results.</param>
    /// <param name="at">Where its key is written.</param>
    /// <param name="nameAt">Where its name is written, when it has one.</param>
    public InferredResultsOverride(ObjectName procedure, string key, string? name, EquatableArray<PathOverride> paths, JsonLocation at, JsonLocation? nameAt)
        : base(procedure, key, name, at, nameAt)
    {
        Paths = paths;
    }

    /// <summary>Overrides of places inside its results.</summary>
    public EquatableArray<PathOverride> Paths { get; }
}
