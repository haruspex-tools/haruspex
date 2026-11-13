using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;

namespace Haruspex.Config;

/// <summary>
/// A procedure's overrides (<c>procedures["schema.name"]</c>): either over the results haruspex infers
/// (<see cref="InferredResultsOverride"/>: path overrides), or results declared in their place
/// (<see cref="DeclaredResultsOverride"/>), never both, since a declaration already says everything a path could. Either may
/// give the procedure its generated name.
/// </summary>
/// <remarks>A closed hierarchy (docs/decisions/0031-the-model-is-closed-hierarchies-of-records.md): a switch over the two is checked.</remarks>
public abstract record ProcedureOverride
{
    private protected ProcedureOverride(ObjectName procedure, string key, string? name, JsonLocation at, JsonLocation? nameAt)
    {
        Procedure = procedure;
        Key = key;
        Name = name;
        At = at;
        NameAt = nameAt;
    }

    /// <summary>The procedure, compared as SQL Server compares names (case, and brackets, which the key may hold).</summary>
    public ObjectName Procedure { get; }

    /// <summary>The key as written, for a problem that names it.</summary>
    public string Key { get; }

    /// <summary>The generated name to use instead of the naming rules', or null.</summary>
    public string? Name { get; }

    /// <summary>Where its key is written.</summary>
    public JsonLocation At { get; }

    /// <summary>Where its name is written, when it has one.</summary>
    public JsonLocation? NameAt { get; }
}
