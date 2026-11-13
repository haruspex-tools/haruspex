using Haruspex.Collections;
using Haruspex.Diagnostics;

namespace Haruspex.Config;

/// <summary>A config file, read and checked: what <c>haruspex.json</c> says, with where each part was written.</summary>
public sealed record HaruspexConfig
{
    /// <summary>The name the file is looked for by.</summary>
    public const string FileName = "haruspex.json";

    /// <summary>The file, as found or named. Problems are reported against it.</summary>
    public required string File { get; init; }

    /// <summary>The file as a whole, for a problem with no place in it; null when there is no file.</summary>
    public FileLocation? Location => File.Length == 0 ? null : new FileLocation(File);

    /// <summary>The folder holding the file, which its relative paths are relative to.</summary>
    public required string Directory { get; init; }

    /// <summary>The folder of <c>.sql</c> files, resolved against <see cref="Directory"/>, or null when the file doesn't say.</summary>
    public string? Source { get; init; }

    /// <summary>The folder to write to, resolved against <see cref="Directory"/>, or null when the file doesn't say.</summary>
    public string? Out { get; init; }

    /// <summary>The settings of each schema (<c>schemas</c>), in the order written: selection, prefix and naming.</summary>
    public EquatableArray<SchemaSettings> Schemas { get; init; } = [];

    /// <summary>How result objects treat keys their type does not list.</summary>
    public ObjectsPolicy Objects { get; init; } = ObjectsPolicy.Strip;

    /// <summary>The naming rules every schema starts from (the top-level <c>naming</c>).</summary>
    public NamingRules Naming { get; init; } = NamingRules.Default;

    /// <summary>The words the generated code's language reserves, which no name settled for it may be; handed in by the CLI.</summary>
    public ReservedWords Reserved { get; init; } = ReservedWords.None;

    /// <summary>Where the top-level <c>naming</c> is written, or null.</summary>
    public JsonLocation? NamingAt { get; init; }

    /// <summary>
    /// The setting that decides how objects of <paramref name="schema"/> are named, for a problem with a name: the schema's
    /// own <c>naming</c>, else the top-level one, else the schema's block. Null when the file says nothing about it.
    /// </summary>
    public JsonLocation? NamingLocation(string schema) => Schema(schema) is { } settings ? settings.Naming?.At ?? NamingAt ?? settings.At : NamingAt;

    /// <summary>Whether any schema has an include, without which nothing is selected.</summary>
    public bool SelectsAnything => Schemas.Any(s => s.Include is not null);

    /// <summary>The settings of the schema named <paramref name="schema"/> (ignoring case, as SQL Server does), or null.</summary>
    public SchemaSettings? Schema(string schema) => Schemas.FirstOrDefault(s => string.Equals(s.Name, schema, StringComparison.OrdinalIgnoreCase));

    /// <summary>The naming rules for objects of <paramref name="schema"/>: the top-level rules with the schema's over them.</summary>
    public NamingRules NamingFor(string schema) => Schema(schema) is { } settings ? Naming.For(settings.Naming, settings.Prefix) : Naming;

    /// <summary>The per-procedure overrides, in the order written.</summary>
    public EquatableArray<ProcedureOverride> Procedures { get; init; } = [];

    /// <summary>The settings that apply when there is no config file: nothing selected, every default.</summary>
    public static HaruspexConfig None(string directory, ReservedWords? reserved = null) =>
        new() { File = "", Directory = directory, Reserved = reserved ?? ReservedWords.None };
}
