using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Sources.Projects;

/// <summary>
/// The properties of one project as MSBuild's first pass sets them, in order, across the project and the files it imports, and
/// how a value's <c>$(Name)</c> references are expanded. Names ignore case, as MSBuild's do.
/// </summary>
/// <remarks>
/// A global property (given on a build's command line) can't be set by the project; here the only one is <c>Configuration</c>
/// when the project is read as another configuration than its own default (<see cref="ProjectEvaluator"/>). What isn't
/// evaluated is kept as a doubt on the value, never guessed: a property function (<c>$([System.IO.Path]::Combine(…))</c>,
/// <c>$(Name.Replace(…))</c>), an item list (<c>@(Build)</c>), metadata (<c>%(Identity)</c>), and a property defined nowhere,
/// which a build would take from the environment or its command line.
/// </remarks>
/// <param name="project">The project's full path, which <c>$(MSBuildProjectDirectory)</c> and the rest name.</param>
/// <param name="globals">The properties a command line sets.</param>
internal sealed partial class ProjectProperties(string project, IReadOnlyDictionary<string, string> globals)
{
    /// <summary>
    /// The properties a build's own command line sets, if anything does, and which a project's conditions test for empty to give
    /// them their defaults (<c>'$(Configuration)' == ''</c>): defined nowhere, they are empty, with no doubt.
    /// </summary>
    private static readonly HashSet<string> CommandLineDefaults = new(StringComparer.OrdinalIgnoreCase) { ProjectXml.Configuration, ProjectXml.Platform };

    private readonly Dictionary<string, Evaluated> values = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> globals = new(globals, StringComparer.OrdinalIgnoreCase);

    /// <summary>The property's value, or null when it is defined nowhere.</summary>
    public Evaluated? this[string name] =>
        globals.TryGetValue(name, out var global) ? Evaluated.Sure(global) : values.GetValueOrDefault(name);

    /// <summary>Sets a property, unless the command line set it, which no project can change.</summary>
    public void Set(string name, Evaluated value)
    {
        if (!globals.ContainsKey(name))
            values[name] = value;
    }

    /// <summary>
    /// Sets a property to <paramref name="value"/> when <paramref name="when"/> holds: as written, when it holds for sure, or
    /// doubted when it may or may not, keeping what it had or the new value as the condition is read, with both values' doubts
    /// and the condition's.
    /// </summary>
    public void Set(string name, Evaluated value, Truth when)
    {
        var before = this[name] ?? Evaluated.Sure("");
        if (when.Doubts.Count == 0)
        {
            if (when.Holds)
                Set(name, value);
            return;
        }

        Set(name, (when.Holds ? value : before).Doubted(Doubt.Union(before.Doubts.Concat(value.Doubts), when.Doubts)));
    }

    /// <summary>Sets a property that is still empty, as the SDK's own defaults do (<c>Condition="'$(Name)' == ''"</c>).</summary>
    public void Default(string name, string value)
    {
        if (this[name] is not { Text.Length: > 0 } and not { IsSure: false })
            Set(name, Evaluated.Sure(value));
    }

    /// <summary>
    /// <paramref name="raw"/> with each <c>$(Name)</c> replaced by its value, read in <paramref name="file"/> at
    /// <paramref name="at"/>, and MSBuild's escapes (<c>%3B</c> for <c>;</c>) undone: with a doubt for each part that isn't
    /// evaluated, which is left out of the text.
    /// </summary>
    public Evaluated Expand(string raw, ProjectFile file, SourceLocation at)
    {
        var text = new StringBuilder();
        var doubts = new List<Doubt>();
        var i = 0;
        while (i < raw.Length)
        {
            if (i + 1 < raw.Length && raw[i + 1] == '(' && raw[i] is '$' or '@' or '%')
            {
                var end = Closing(raw, i + 1);
                if (end < 0)
                {
                    doubts.Add(new Doubt(at, $"\"{raw[i..]}\" has no closing parenthesis"));
                    break;
                }

                var inner = raw[(i + 2)..end];
                switch (raw[i])
                {
                    case '$' when Name().IsMatch(inner):
                        var value = Lookup(inner.Trim(), file, at);
                        text.Append(value.Text);
                        doubts.AddRange(value.Doubts);
                        break;
                    case '$':
                        doubts.Add(new Doubt(at, $"the property function $({inner}) isn't evaluated"));
                        break;
                    case '@':
                        doubts.Add(new Doubt(at, $"the item list @({inner}) isn't evaluated"));
                        break;
                    default:
                        doubts.Add(new Doubt(at, $"the metadata %({inner}) isn't evaluated"));
                        break;
                }

                i = end + 1;
                continue;
            }

            text.Append(raw[i]);
            i++;
        }

        return new Evaluated(Unescape(text.ToString()), [.. doubts.Distinct()]);
    }

    /// <summary>The value of <c>$(name)</c> in <paramref name="file"/>: MSBuild's reserved properties, then the project's.</summary>
    private Evaluated Lookup(string name, ProjectFile file, SourceLocation at)
    {
        if (Reserved(name, file) is { } reserved)
            return Evaluated.Sure(reserved);
        if (this[name] is { } value)
            return value;
        return CommandLineDefaults.Contains(name)
            ? Evaluated.Sure("")
            : new Evaluated("", [Doubt.DefinedNowhere(at, name)]);
    }

    /// <summary>The reserved properties MSBuild gives every project and every file it reads, which a project can't set.</summary>
    private string? Reserved(string name, ProjectFile file) => ReservedProperties.TryGetValue(name, out var value) ? value(project, file) : null;

    /// <summary>Each reserved property's value, from the project's path and the file being read.</summary>
    private static readonly Dictionary<string, Func<string, ProjectFile, string?>> ReservedProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        [ProjectXml.MSBuildProjectDirectory] = (project, _) => Path.GetDirectoryName(project),
        [ProjectXml.MSBuildProjectFullPath] = (project, _) => project,
        [ProjectXml.MSBuildProjectFile] = (project, _) => Path.GetFileName(project),
        [ProjectXml.MSBuildProjectName] = (project, _) => Path.GetFileNameWithoutExtension(project),
        [ProjectXml.MSBuildProjectExtension] = (project, _) => Path.GetExtension(project),
        [ProjectXml.MSBuildThisFileDirectory] = (_, file) => file.Folder + Path.DirectorySeparatorChar,
        [ProjectXml.MSBuildThisFileFullPath] = (_, file) => file.Path,
        [ProjectXml.MSBuildThisFile] = (_, file) => Path.GetFileName(file.Path),
        [ProjectXml.MSBuildThisFileName] = (_, file) => Path.GetFileNameWithoutExtension(file.Path),
        [ProjectXml.MSBuildThisFileExtension] = (_, file) => Path.GetExtension(file.Path),
    };

    /// <summary>The index of the parenthesis that closes the one at <paramref name="open"/>, past any nested in it and any quoted text; -1 when none.</summary>
    internal static int Closing(string text, int open)
    {
        var depth = 0;
        char? quote = null;
        for (var i = open; i < text.Length; i++)
        {
            var c = text[i];
            if (quote is { } q)
            {
                if (c == q)
                    quote = null;
                continue;
            }

            switch (c)
            {
                case '\'' or '"' or '`' when depth > 0:
                    quote = c;
                    break;
                case '(':
                    depth++;
                    break;
                case ')' when --depth == 0:
                    return i;
            }
        }

        return -1;
    }

    /// <summary>MSBuild's escapes undone: <c>%</c> and two hexadecimal digits is the character they code.</summary>
    private static string Unescape(string text) =>
        Escape().Replace(text, m => ((char)int.Parse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToString());

    /// <summary>A property's name, as <c>$(…)</c> holds one that isn't a function.</summary>
    [GeneratedRegex(@"^\s*[A-Za-z_][A-Za-z0-9_\-]*\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex Name();

    [GeneratedRegex("%([0-9A-Fa-f]{2})", RegexOptions.CultureInvariant)]
    private static partial Regex Escape();
}
