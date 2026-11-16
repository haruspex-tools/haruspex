using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Sources.Projects;

/// <summary>An item as MSBuild's evaluation leaves it: its type, what it names, and its metadata.</summary>
/// <remarks>A working type of one evaluation, which nothing compares.</remarks>
/// <param name="Type">The item type, such as <c>Build</c>.</param>
/// <param name="Spec">What it names, as written once its properties are expanded: a path, or for an item that isn't a file (a package) its name.</param>
/// <param name="FullPath">The full path of the file it names, made from <paramref name="Spec"/>; null for an item that isn't a file, or a path rooted the way only Windows roots one, read elsewhere.</param>
/// <param name="At">The element that included it.</param>
internal sealed class ProjectItem(string Type, string Spec, string? FullPath, SourceLocation At)
{
    /// <summary>The item type.</summary>
    public string Type { get; } = Type;

    /// <summary>What it names.</summary>
    public string Spec { get; } = Spec;

    /// <summary>The full path of the file it names, or null.</summary>
    public string? FullPath { get; } = FullPath;

    /// <summary>The element that included it.</summary>
    public SourceLocation At { get; } = At;

    /// <summary>
    /// Why a build can't expand the pattern it was included by, which it then names as a file's name (<see cref="Glob.Files"/>);
    /// null for any other item.
    /// </summary>
    public string? Unexpanded { get; init; }

    /// <summary>Its metadata, by name ignoring case, as MSBuild names metadata.</summary>
    public Dictionary<string, Evaluated> Metadata { get; } = new(StringComparer.OrdinalIgnoreCase);
}
