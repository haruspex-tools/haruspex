using System.Text;
using System.Text.RegularExpressions;

namespace Haruspex.SqlServer.Sources.Projects;

/// <summary>
/// An item's path with MSBuild's wildcards, relative to the project's folder: <c>*</c> any characters of one name, <c>?</c> one,
/// and <c>**</c> any number of folders, none included (<c>**/*.sql</c> holds <c>a.sql</c>). Either slash separates names.
/// </summary>
/// <remarks>
/// A pattern matches ignoring case, everywhere: a build on Linux was observed to do so too, for the SDK's <c>**/*.sql</c>
/// (<c>UPPER.SQL</c> is built) and for <c>Remove</c> (<c>X\Gone.sql</c> removes <c>x/gone.sql</c>). A pattern holds hidden folders
/// (<c>.hidden/h.sql</c> is built, observed) and leaves nothing out of its own: what is left out is the item's <c>Exclude</c>.
/// </remarks>
internal sealed class Glob
{
    /// <summary>The folder the pattern's first wildcard is under, full; the files it can match are under it.</summary>
    private readonly string start;

    /// <summary>What a file's path relative to <see cref="start"/>, with <c>/</c>, must match.</summary>
    private readonly Regex rest;

    private Glob(string start, Regex rest)
    {
        this.start = start;
        this.rest = rest;
    }

    /// <summary>Whether <paramref name="spec"/> holds a wildcard, so that it names files by a pattern rather than one file.</summary>
    public static bool IsPattern(string spec) => spec.IndexOfAny(['*', '?']) >= 0;

    /// <summary>The pattern <paramref name="spec"/>, relative to <paramref name="folder"/>.</summary>
    public static Glob Of(string folder, string spec)
    {
        var parts = spec.Replace('\\', '/').Split('/');
        var fixedParts = parts.TakeWhile(p => !IsPattern(p)).ToList();
        var start = DiskPath.Full(folder, string.Join('/', fixedParts));
        var pattern = new StringBuilder("^");
        var wild = parts.Skip(fixedParts.Count).Where(p => p.Length > 0).ToList();
        for (var i = 0; i < wild.Count; i++)
        {
            var last = i == wild.Count - 1;
            if (wild[i] == "**")
            {
                pattern.Append(last ? ".*" : "(?:.*/)?");
                continue;
            }

            foreach (var c in wild[i])
                pattern.Append(c switch { '*' => "[^/]*", '?' => "[^/]", _ => Regex.Escape(c.ToString()) });
            if (!last)
                pattern.Append('/');
        }

        return new Glob(start, new Regex(pattern.Append('$').ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
    }

    /// <summary>Every file the pattern names, as full paths in ordinal order; none when its folder isn't there.</summary>
    public IEnumerable<string> Files()
    {
        if (DiskPath.Locate(start, start, out _) is not { } folder || !Directory.Exists(folder))
            return [];
        return Walk(folder)
            .Where(file => rest.IsMatch(Path.GetRelativePath(folder, file).Replace('\\', '/')))
            .Order(StringComparer.Ordinal);
    }

    /// <summary>
    /// Every file under <paramref name="top"/>, hidden ones included, each folder entered once by where it really is: a link to a
    /// folder elsewhere is followed, as a build follows it, and a link back to a folder already walked (a loop) isn't, where
    /// enumerating through it would find each file again and again (a build reads the file once, observed).
    /// </summary>
    private static List<string> Walk(string top)
    {
        var options = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true };
        var files = new List<string>();
        var walked = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>([top]);
        while (pending.TryPop(out var folder))
        {
            if (!walked.Add(Real(folder)))
                continue;
            files.AddRange(Directory.EnumerateFiles(folder, "*", options));
            foreach (var inner in Directory.EnumerateDirectories(folder, "*", options))
                pending.Push(inner);
        }

        return files;
    }

    /// <summary>Where a folder really is, every link on its path followed.</summary>
    private static string Real(string folder)
    {
        var info = new DirectoryInfo(folder);
        var real = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? info.FullName;
        var parent = Path.GetDirectoryName(real);
        return parent is null || parent == real ? real : Path.Join(Real(parent), Path.GetFileName(real));
    }

    /// <summary>Whether the file at <paramref name="path"/>, full, is one the pattern names.</summary>
    public bool Matches(string path)
    {
        // Under the pattern's folder ignoring case, as the rest is matched.
        var folder = Path.EndsInDirectorySeparator(start) ? start : start + Path.DirectorySeparatorChar;
        return path.StartsWith(folder, StringComparison.OrdinalIgnoreCase) && rest.IsMatch(path[folder.Length..].Replace('\\', '/'));
    }
}
