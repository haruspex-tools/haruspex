using Haruspex.Collections;
using Haruspex.Diagnostics;

namespace Haruspex.Config;

/// <summary>A module a split lists: its name, the schemas whose procedures it holds, and where it is written.</summary>
/// <param name="Name">The module's name, which its files are named after.</param>
/// <param name="Schemas">The schemas whose selected procedures it holds, as written; empty for a module of query files alone.</param>
/// <param name="At">Where its key is written.</param>
public sealed record SplitModule(string Name, EquatableArray<string> Schemas, JsonLocation At);
