using Haruspex.Collections;
using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Sources.Projects;

/// <summary>
/// An item element that wasn't applied, because what it includes, removes or updates, or whether it applies, isn't evaluated.
/// The reader decides what that touches (<c>ProjectFiles</c>, <c>ProjectReferences</c>): a script it may add or take out isn't
/// read, and a reference it may make is reported. A working type of one evaluation, which nothing compares.
/// </summary>
/// <param name="Type">The item type, such as <c>Build</c>.</param>
/// <param name="Verb">How it applies: <c>Include</c>, <c>Remove</c> or <c>Update</c>.</param>
/// <param name="Written">What it names, as written, before any property is expanded.</param>
/// <param name="Spec">What it names, expanded, when that is known (only whether it applies is doubted); null when it isn't.</param>
/// <param name="At">The element.</param>
/// <param name="Doubts">What isn't evaluated.</param>
/// <param name="Metadata">
/// The element's own metadata, as far as it is evaluated: what a reference says of its database even when what it names isn't
/// known, as in Visual Studio's reference to <c>$(DacPacRootPath)\…\master.dacpac</c>.
/// </param>
internal sealed class DoubtedItem(string Type, string Verb, string Written, string? Spec, SourceLocation At, EquatableArray<Doubt> Doubts, IReadOnlyDictionary<string, Evaluated> Metadata)
{
    /// <summary>The item type.</summary>
    public string Type { get; } = Type;

    /// <summary>How it applies.</summary>
    public string Verb { get; } = Verb;

    /// <summary>What it names, as written.</summary>
    public string Written { get; } = Written;

    /// <summary>What it names, expanded, or null.</summary>
    public string? Spec { get; } = Spec;

    /// <summary>The element.</summary>
    public SourceLocation At { get; } = At;

    /// <summary>What isn't evaluated.</summary>
    public EquatableArray<Doubt> Doubts { get; } = Doubts;

    /// <summary>The element's own metadata.</summary>
    public IReadOnlyDictionary<string, Evaluated> Metadata { get; } = Metadata;
}
