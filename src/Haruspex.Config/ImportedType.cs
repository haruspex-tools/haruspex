namespace Haruspex.Config;

/// <summary>
/// The type a path override puts in place of the inferred one (<c>"type"</c> with its <c>"import"</c>): the two are one
/// setting, which the reader refuses apart.
/// </summary>
/// <param name="Name">An exported schema's name, with <c>[]</c> for each level of array, such as <c>TagSchema[]</c>.</param>
/// <param name="From">The module that exports it, as written.</param>
public sealed record ImportedType(string Name, string From);
