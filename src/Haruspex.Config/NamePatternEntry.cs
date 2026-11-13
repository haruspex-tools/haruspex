using Haruspex.Diagnostics;

namespace Haruspex.Config;

/// <summary>An include or exclude pattern inside a schema's block, matching the object's own name, with where it is written.</summary>
public sealed record NamePatternEntry(NamePattern Pattern, JsonLocation At);
