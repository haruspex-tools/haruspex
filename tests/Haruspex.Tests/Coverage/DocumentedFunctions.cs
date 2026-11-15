namespace Haruspex.Tests.Coverage;

/// <summary>
/// The built-in functions Microsoft's T-SQL reference lists for SQL Server 2025, by its categories:
/// <c>tests/coverage/sql-server-2025-functions.txt</c>, names only. The coverage page lists each against the catalogue, so a
/// function the reference has and haruspex doesn't model is listed rather than found by accident.
/// </summary>
/// <param name="Categories">The reference's categories, in the file's order.</param>
internal sealed record DocumentedFunctions(IReadOnlyList<DocumentedFunctions.Category> Categories)
{
    /// <summary>The file, from the repository's root.</summary>
    public const string Path = "tests/coverage/sql-server-2025-functions.txt";

    /// <summary>The functions as the file lists them.</summary>
    public static DocumentedFunctions Current { get; } = Read(File.ReadAllLines(System.IO.Path.Combine(RepositoryPaths.Root, Path)));

    /// <summary>Every name, once, in the order first listed.</summary>
    public IEnumerable<string> Names => Categories.SelectMany(c => c.Names).Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Reads the file: a <c>[heading]</c> starts a category, the comment before it is its page, and each other line is a
    /// name. Comments elsewhere and blank lines are skipped.
    /// </summary>
    public static DocumentedFunctions Read(IEnumerable<string> lines)
    {
        var categories = new List<(string Name, string Source, List<string> Names)>();
        string? source = null;
        foreach (var line in lines.Select(l => l.Trim()).Where(l => l.Length > 0))
        {
            if (line.StartsWith('#'))
                source = line.TrimStart('#').Trim();
            else if (line.StartsWith('[') && line.EndsWith(']'))
                categories.Add((line[1..^1], source ?? "", []));
            else if (categories.Count == 0)
                throw new FormatException($"{Path}: the name {line} comes before any category.");
            else
                categories[^1].Names.Add(line);
        }

        return new DocumentedFunctions([.. categories.Select(c => new Category(c.Name, c.Source, [.. c.Names]))]);
    }

    /// <summary>One of the reference's categories.</summary>
    /// <param name="Name">Its title, in sentence case.</param>
    /// <param name="Source">The page its names were read from.</param>
    /// <param name="Names">The functions it lists, as the reference writes them.</param>
    public sealed record Category(string Name, string Source, IReadOnlyList<string> Names);
}
