using System.Text.RegularExpressions;

namespace Haruspex.Config;

/// <summary>A config file, read and checked: what <c>haruspex.json</c> says, with where each part was written.</summary>
public sealed record HaruspexConfig
{
    /// <summary>The name the file is looked for by.</summary>
    public const string FileName = "haruspex.json";

    /// <summary>The file, as found or named. Problems are reported against it.</summary>
    public required string File { get; init; }

    /// <summary>The folder holding the file, which its relative paths are relative to.</summary>
    public required string Directory { get; init; }

    /// <summary>The folder of <c>.sql</c> files, resolved against <see cref="Directory"/>, or null when the file doesn't say.</summary>
    public string? Source { get; init; }

    /// <summary>The folder to write to, resolved against <see cref="Directory"/>, or null when the file doesn't say.</summary>
    public string? Out { get; init; }

    /// <summary>The settings of each schema (<c>schemas</c>), in the order written: selection, prefix and naming.</summary>
    public IReadOnlyList<SchemaSettings> Schemas { get; init; } = [];

    /// <summary>How result objects treat keys their type does not list.</summary>
    public ObjectsPolicy Objects { get; init; } = ObjectsPolicy.Strip;

    /// <summary>The naming rules every schema starts from (the top-level <c>naming</c>).</summary>
    public NamingRules Naming { get; init; } = NamingRules.Default;

    /// <summary>Where the top-level <c>naming</c> is written, or null.</summary>
    public ConfigLocation? NamingAt { get; init; }

    /// <summary>
    /// The setting that decides how objects of <paramref name="schema"/> are named, for a problem with a name: the schema's
    /// own <c>naming</c>, else the top-level one, else the schema's block. Null when the file says nothing about it.
    /// </summary>
    public ConfigLocation? NamingLocation(string schema) => Schema(schema) is { } settings ? settings.Naming?.At ?? NamingAt ?? settings.At : NamingAt;

    /// <summary>Whether any schema has an include, without which nothing is selected.</summary>
    public bool SelectsAnything => Schemas.Any(s => s.Include is not null);

    /// <summary>The settings of the schema named <paramref name="schema"/> (ignoring case, as SQL Server does), or null.</summary>
    public SchemaSettings? Schema(string schema) => Schemas.FirstOrDefault(s => string.Equals(s.Name, schema, StringComparison.OrdinalIgnoreCase));

    /// <summary>The naming rules for objects of <paramref name="schema"/>: the top-level rules with the schema's over them.</summary>
    public NamingRules NamingFor(string schema) => Schema(schema) is { } settings ? Naming.For(settings.Naming, settings.Prefix) : Naming;

    /// <summary>The per-procedure overrides, in the order written.</summary>
    public IReadOnlyList<ProcedureOverride> Procedures { get; init; } = [];

    /// <summary>The settings that apply when there is no config file: nothing selected, every default.</summary>
    public static HaruspexConfig None(string directory) => new() { File = "", Directory = directory };
}

/// <summary>An include or exclude pattern inside a schema's block, matching the object's own name, with where it is written.</summary>
public sealed record NamePatternEntry(NamePattern Pattern, ConfigLocation At);

/// <summary>
/// One schema's settings (<c>schemas["sales"]</c>). A block is an object so that settings can be added to it later (one
/// output module per schema, say) without breaking a file written today.
/// </summary>
/// <param name="Name">The schema's name exactly as in the database: no brackets, dots allowed.</param>
/// <param name="At">Where its key is written.</param>
public sealed record SchemaSettings(string Name, ConfigLocation At)
{
    /// <summary>Which of its procedures to type, by their own names; null when the block has no include, so it selects nothing.</summary>
    public IReadOnlyList<NamePatternEntry>? Include { get; init; }

    /// <summary>Which of the included procedures to leave out.</summary>
    public IReadOnlyList<NamePatternEntry> Exclude { get; init; } = [];

    /// <summary>Put in front of the names of its objects, before the case rules; empty by default.</summary>
    public string Prefix { get; init; } = "";

    /// <summary>Its naming settings over the top-level ones, or null.</summary>
    public SchemaNaming? Naming { get; init; }
}

/// <summary>A schema's <c>naming</c>: its <c>case</c> replaces the top-level one, its <c>replace</c> rules run after the top-level ones.</summary>
public sealed record SchemaNaming(IReadOnlyList<NamingReplacement> Replace, NamingCase? Case)
{
    /// <summary>Where it is written.</summary>
    public ConfigLocation? At { get; init; }
}

/// <summary>How result objects treat keys their type does not list (<c>"objects"</c>).</summary>
public enum ObjectsPolicy
{
    /// <summary>Unknown keys are removed when parsing (<c>z.object</c>). The default, for deployment safety.</summary>
    Strip,

    /// <summary>An unknown key fails parsing (<c>z.strictObject</c>).</summary>
    Strict,
}

/// <summary>The case style of generated names (<c>naming.case</c>).</summary>
public enum NamingCase
{
    /// <summary><c>GetOrder</c>: every word capitalised; <c>$</c>, <c>#</c> and <c>_</c> break words. The default.</summary>
    Pascal,

    /// <summary><c>getOrder</c>: as Pascal, with the first letter lower case.</summary>
    Camel,

    /// <summary>The name as it is after the replacements, with any prefix: it must be an identifier already.</summary>
    Preserve,
}

/// <summary>One <c>naming.replace</c> rule: a regular expression and its replacement.</summary>
/// <param name="Pattern">The compiled expression.</param>
/// <param name="With">The replacement, where <c>$1</c> names a group.</param>
/// <param name="At">Where the rule is written.</param>
public sealed record NamingReplacement(Regex Pattern, string With, ConfigLocation? At);

/// <summary>A procedure's overrides (<c>procedures["schema.name"]</c>).</summary>
/// <param name="Procedure">The procedure, as a pattern without wildcards (so brackets and case work as in selection).</param>
/// <param name="Name">The generated name to use instead of the naming rules', or null.</param>
/// <param name="Paths">Overrides of places inside its results.</param>
/// <param name="At">Where its key is written.</param>
/// <param name="NameAt">Where its name is written, when it has one.</param>
public sealed record ProcedureOverride(ObjectPattern Procedure, string? Name, IReadOnlyList<PathOverride> Paths, ConfigLocation At, ConfigLocation? NameAt);

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
