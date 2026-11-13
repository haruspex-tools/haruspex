using Haruspex.Collections;
using Haruspex.Diagnostics;

namespace Haruspex.Config;

/// <summary>
/// One schema's settings (<c>schemas["sales"]</c>). A block is an object so that settings can be added to it later (one
/// output module per schema, say) without breaking a file written today.
/// </summary>
/// <param name="Name">The schema's name exactly as in the database: no brackets, dots allowed.</param>
/// <param name="At">Where its key is written.</param>
public sealed record SchemaSettings(string Name, JsonLocation At)
{
    /// <summary>Which of its procedures to type, by their own names; null when the block has no include, so it selects nothing.</summary>
    public EquatableArray<NamePatternEntry>? Include { get; init; }

    /// <summary>Which of the included procedures to leave out.</summary>
    public EquatableArray<NamePatternEntry> Exclude { get; init; } = [];

    /// <summary>Put in front of the names of its objects, before the case rules; empty by default.</summary>
    public string Prefix { get; init; } = "";

    /// <summary>Its naming settings over the top-level ones, or null.</summary>
    public SchemaNaming? Naming { get; init; }
}
