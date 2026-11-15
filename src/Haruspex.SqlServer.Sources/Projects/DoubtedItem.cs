using Haruspex.Collections;
using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Sources.Projects;

/// <summary>
/// An item element that wasn't applied, because what it includes, removes or updates, or whether it applies, isn't evaluated.
/// The reader decides what that touches (<c>ProjectFiles</c>, <c>ProjectReferences</c>): a script it may add or take out isn't
/// read, and a reference it may make is reported.
/// </summary>
/// <param name="Type">The item type, such as <c>Build</c>.</param>
/// <param name="Verb">How it applies: <c>Include</c>, <c>Remove</c> or <c>Update</c>.</param>
/// <param name="Written">What it names, as written, before any property is expanded.</param>
/// <param name="Spec">What it names, expanded, when that is known (only whether it applies is doubted); null when it isn't.</param>
/// <param name="At">The element.</param>
/// <param name="Doubts">What isn't evaluated.</param>
internal sealed record DoubtedItem(string Type, string Verb, string Written, string? Spec, SourceLocation At, EquatableArray<Doubt> Doubts);
