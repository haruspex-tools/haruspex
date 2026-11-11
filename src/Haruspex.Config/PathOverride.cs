namespace Haruspex.Config;

/// <summary>An override of one place inside a procedure's results (<c>paths["lines[].sku"]</c>).</summary>
/// <param name="Path">The path as written.</param>
/// <param name="At">Where the path is written.</param>
public sealed record PathOverride(string Path, ConfigLocation At)
{
    /// <summary>The name for the object type at the path.</summary>
    public string? Name { get; init; }

    /// <summary>Whether the value is declared never null (<c>"nullable": false</c>).</summary>
    public bool NotNull { get; init; }

    /// <summary>Why the claim holds: required for a nullability or a value set.</summary>
    public string? Why { get; init; }

    /// <summary>The type to use, an exported schema's name with <c>[]</c> for each level of array.</summary>
    public string? Type { get; init; }

    /// <summary>The module that exports <see cref="Type"/>.</summary>
    public string? Import { get; init; }

    /// <summary>Whether the value set at the path is declared exact (<c>"values": "exact"</c>).</summary>
    public bool ExactValues { get; init; }
}
