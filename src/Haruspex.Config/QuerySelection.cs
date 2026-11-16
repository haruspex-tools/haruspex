using System.Text;
using System.Text.RegularExpressions;
using Haruspex.Diagnostics;

namespace Haruspex.Config;

/// <summary>
/// The query files a config file's <c>queries</c> names: each entry a folder (every <c>.sql</c> file below it), a file, or a
/// pattern (<c>*</c> any characters but <c>/</c>, <c>**</c> any folders, <c>?</c> one character), relative to the config file's
/// folder. A query file belongs to the module whose config file is nearest above it, so a folder below with a
/// <c>haruspex.json</c> of its own is that module's and is passed over.
/// </summary>
/// <remarks>
/// An entry that names no file of this module is an error, as an include that selects nothing is: a typo would otherwise drop
/// the functions of the files it meant. Hidden folders and <c>node_modules</c> are passed over, as <c>--all</c> passes them over
/// looking for config files. Files are compared by their paths as written, so on a file system that ignores case a pattern must
/// still be written in the case of the files it names.
/// </remarks>
public static class QuerySelection
{
    /// <summary>Every query file the config file's entries name, each once, in ordinal order of its relative path; or every entry that names none.</summary>
    public static Outcome<IReadOnlyList<QueryFileFound>> Select(HaruspexConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config.Queries.Count == 0)
            return new Succeeded<IReadOnlyList<QueryFileFound>>([]);

        var own = Own(config.Directory).ToList();
        var others = OtherModules(config.Directory);
        var found = new SortedDictionary<string, QueryFileFound>(StringComparer.Ordinal);
        var problems = new List<Diagnostic>();
        foreach (var entry in config.Queries)
        {
            var named = Named(entry, own);
            if (named.Count == 0)
            {
                problems.Add(ConfigCodes.QueriesMatchNothing.At(entry.At, $"\"{entry.Text}\" names no query file of this module{Why(entry, others)}. Correct it, or remove it."));
                continue;
            }

            foreach (var relative in named)
                found.TryAdd(relative, new QueryFileFound(Path.Combine(config.Directory, relative), relative));
        }

        return Outcome.Of<IReadOnlyList<QueryFileFound>>([.. found.Values], problems);
    }

    /// <summary>The module's own files that an entry names.</summary>
    private static List<string> Named(QueryPattern entry, List<string> own)
    {
        if (entry.IsPattern)
        {
            var pattern = Pattern(entry.Text);
            return [.. own.Where(f => pattern.IsMatch(f))];
        }

        if (entry.Text == ".")
            return own;
        var folder = entry.Text + "/";
        return [.. own.Where(f => f == entry.Text || f.StartsWith(folder, StringComparison.Ordinal))];
    }

    /// <summary>Why an entry names nothing, when it is because the files are another module's.</summary>
    private static string Why(QueryPattern entry, List<string> others)
    {
        var target = entry.IsPattern ? null : entry.Text;
        var theirs = others.FirstOrDefault(o => target is not null && (target == o || target.StartsWith(o + "/", StringComparison.Ordinal)));
        return theirs is null ? "" : $": {theirs} holds a {HaruspexConfig.FileName} of its own, so the query files below it are that module's";
    }

    /// <summary>
    /// The <c>.sql</c> files below the config file's folder that are this module's, relative to it: none below a folder holding a
    /// config file of its own, a hidden folder or <c>node_modules</c>.
    /// </summary>
    private static IEnumerable<string> Own(string directory)
    {
        var pending = new Stack<string>([directory]);
        while (pending.Count > 0)
        {
            var folder = pending.Pop();
            foreach (var file in Directory.EnumerateFiles(folder, "*.sql"))
                yield return Relative(directory, file);
            foreach (var below in Directory.EnumerateDirectories(folder).Where(Followed))
                pending.Push(below);
        }
    }

    /// <summary>The folders below the config file's that hold one of their own, relative to it: another module's.</summary>
    private static List<string> OtherModules(string directory)
    {
        var modules = new List<string>();
        var pending = new Stack<string>(Directory.EnumerateDirectories(directory).Where(d => !Hidden(d)));
        while (pending.Count > 0)
        {
            var folder = pending.Pop();
            if (File.Exists(Path.Combine(folder, HaruspexConfig.FileName)))
            {
                modules.Add(Relative(directory, folder));
                continue;
            }

            foreach (var below in Directory.EnumerateDirectories(folder).Where(d => !Hidden(d)))
                pending.Push(below);
        }

        return modules;
    }

    /// <summary>Whether a folder's files may be this module's: not hidden, and not holding a config file of its own.</summary>
    private static bool Followed(string folder) => !Hidden(folder) && !File.Exists(Path.Combine(folder, HaruspexConfig.FileName));

    private static bool Hidden(string folder)
    {
        var name = Path.GetFileName(folder);
        return name.StartsWith('.') || name == "node_modules" || (File.GetAttributes(folder) & (FileAttributes.Hidden | FileAttributes.ReparsePoint)) != 0;
    }

    private static string Relative(string directory, string path) => Path.GetRelativePath(directory, path).Replace('\\', '/');

    /// <summary>A pattern as a regular expression over a relative path: <c>**/</c> any folders, <c>**</c> anything, <c>*</c> anything but <c>/</c>, <c>?</c> one character but <c>/</c>.</summary>
    private static Regex Pattern(string pattern)
    {
        var expression = new StringBuilder("^");
        for (var i = 0; i < pattern.Length; i++)
        {
            switch (pattern[i])
            {
                case '*' when pattern.AsSpan(i).StartsWith("**/"):
                    expression.Append("(?:.*/)?");
                    i += 2;
                    break;
                case '*' when i + 1 < pattern.Length && pattern[i + 1] == '*':
                    expression.Append(".*");
                    i++;
                    break;
                case '*':
                    expression.Append("[^/]*");
                    break;
                case '?':
                    expression.Append("[^/]");
                    break;
                default:
                    expression.Append(Regex.Escape(pattern[i].ToString()));
                    break;
            }
        }

        return new Regex(expression.Append('$').ToString(), RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    }
}
