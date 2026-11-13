using Haruspex.Collections;

namespace Haruspex.Config;

/// <summary>A schema's <c>naming</c>: its <c>case</c> replaces the top-level one, its <c>replace</c> rules run after the top-level ones.</summary>
public sealed record SchemaNaming(EquatableArray<NamingReplacement> Replace, NamingCase? Case)
{
    /// <summary>Where it is written.</summary>
    public ConfigLocation? At { get; init; }
}
