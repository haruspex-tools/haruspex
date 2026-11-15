using Haruspex.Collections;
using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Sources.Projects;

/// <summary>
/// An item element that wasn't applied, because what it includes, removes or updates, or whether it applies, isn't evaluated:
/// reported, by the reader, when it is of a type the build reads.
/// </summary>
/// <param name="Type">The item type, such as <c>Build</c>.</param>
/// <param name="At">The element.</param>
/// <param name="Doubts">What isn't evaluated.</param>
internal sealed record DoubtedItem(string Type, SourceLocation At, EquatableArray<Doubt> Doubts);
