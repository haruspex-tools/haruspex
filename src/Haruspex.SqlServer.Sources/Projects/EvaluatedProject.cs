using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Sources.Projects;

/// <summary>
/// A project as <see cref="ProjectEvaluator"/> leaves it: its properties and items, and what wasn't evaluated. A working type of
/// one read, which nothing compares.
/// </summary>
/// <param name="File">The project file.</param>
/// <param name="Format">Which kind of database project it is.</param>
/// <param name="Properties">Its properties, at the end of the first pass.</param>
/// <param name="Items">Its items, at the end of the second, in order.</param>
/// <param name="Doubted">The item elements not applied because something in them isn't evaluated.</param>
/// <param name="Problems">What can't be confined to a value or an item: an import not read, a target.</param>
internal sealed class EvaluatedProject(
    ProjectFile File, ProjectFormat Format, ProjectProperties Properties, IReadOnlyList<ProjectItem> Items, IReadOnlyList<DoubtedItem> Doubted, IReadOnlyList<Diagnostic> Problems)
{
    /// <summary>The project file.</summary>
    public ProjectFile File { get; } = File;

    /// <summary>Which kind of database project it is.</summary>
    public ProjectFormat Format { get; } = Format;

    /// <summary>Its properties.</summary>
    public ProjectProperties Properties { get; } = Properties;

    /// <summary>Its items.</summary>
    public IReadOnlyList<ProjectItem> Items { get; } = Items;

    /// <summary>The item elements not applied.</summary>
    public IReadOnlyList<DoubtedItem> Doubted { get; } = Doubted;

    /// <summary>What can't be confined to a value or an item.</summary>
    public IReadOnlyList<Diagnostic> Problems { get; } = Problems;

    /// <summary>The items of <paramref name="type"/>, in order.</summary>
    public IEnumerable<ProjectItem> OfType(string type) => Items.Where(i => i.Type.Equals(type, StringComparison.OrdinalIgnoreCase));
}
