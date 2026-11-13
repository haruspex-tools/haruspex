using Haruspex.Collections;

namespace Haruspex.Config;

/// <summary>A procedure's overrides (<c>procedures["schema.name"]</c>).</summary>
/// <param name="Procedure">The procedure, as a pattern without wildcards (so brackets and case work as in selection).</param>
/// <param name="Name">The generated name to use instead of the naming rules', or null.</param>
/// <param name="Paths">Overrides of places inside its results.</param>
/// <param name="At">Where its key is written.</param>
/// <param name="NameAt">Where its name is written, when it has one.</param>
public sealed record ProcedureOverride(ObjectPattern Procedure, string? Name, EquatableArray<PathOverride> Paths, ConfigLocation At, ConfigLocation? NameAt)
{
    /// <summary>
    /// The procedure's declared results (<c>results</c>, with its <c>why</c>), or null. A procedure need not be selected, or
    /// in the project, to have them: a call of it contributes them to its caller.
    /// </summary>
    public ResultsDeclaration? Results { get; init; }
}
