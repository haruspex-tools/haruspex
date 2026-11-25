using System.Collections.Immutable;
using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Sources.Projects;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Sources;

/// <summary>
/// The pre- and post-deployment scripts of a project or a package, read for what they change and never for what they define.
/// </summary>
/// <remarks>
/// <para>
/// A deployment runs its pre-deployment script before it deploys the model and its post-deployment script after, every time. So
/// what the post-deployment script does to the model stays done in every database deployed: a check it turns off to seed a table
/// is off, and a procedure it alters is its <c>ALTER</c>. Neither is in the model a build makes, which holds only what the
/// project's scripts define. Seeding with the checks off, and installing a procedure as a stub that a later script alters, are
/// both common ways to write one. A pre-deployment script runs before the model is deployed, and what it does may stand too (a
/// table it creates, a check it disables that the deployment doesn't turn on again), so it is read the same way: whatever either
/// does is a doubt, which claims less.
/// </para>
/// <para>
/// Each is read by the same walk as any script (<see cref="WeakeningStatements"/>), as a deployment script: every statement in
/// it that may disable a check or a key, every table it alters, drops or creates, and every procedure, function and view it
/// creates, alters or drops, which becomes <c>redefined</c>. A project's script is read with each file it includes by
/// <c>:r</c> read apart, so that a location names the file a person can open; a package's has its includes in its text already,
/// as its build wrote them.
/// </para>
/// <para>
/// One that doesn't parse (another sqlcmd command, a variable whose value is the deployment's), or an include that can't be found
/// or read, is a warning (HAR1021), and every object whose name it holds is doubted (<see cref="UnreadDeployment"/>). So is one
/// that changes an object it names through a variable no <c>:setvar</c> before it sets, and then every object is doubted: the
/// deployment gives the value, so the name may be any object's. A project's include is read with the variables set before its
/// <c>:r</c>, as sqlcmd keeps one set for a script and what it includes.
/// </para>
/// </remarks>
internal static class DeploymentScripts
{
    /// <summary>
    /// What a reference to a SQLCMD variable that no <c>:setvar</c> before it sets is read as: a name no object has, so that a
    /// statement through one is found by its name afterwards (<see cref="ThroughVariable"/>). The deployment gives the value, and
    /// a project's <c>SqlCmdVariable</c> default is only a default, so the name isn't known.
    /// </summary>
    private const string Unset = "haruspex_sqlcmd_variable_unset";

    /// <summary>Reads one deployment script's text into <paramref name="into"/>, as a package holds it: its includes inlined.</summary>
    /// <param name="text">The script, as it is in its file or its package.</param>
    /// <param name="file">Its file, which every location names.</param>
    /// <param name="into">What the source declares.</param>
    public static void Read(string text, string file, SchemaDeclarations into)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(into);
        Sent(SqlcmdIncludes.Without(SqlcmdVariables.Substitute(text, SqlcmdVariables.Values(), null, Unset)), file, into);
    }

    /// <summary>
    /// A project's deployment script, from its item, with every file it includes by <c>:r</c>, each read apart and named relative
    /// to <paramref name="folder"/>, as the project's scripts are, and with the variables set where it is included. Each file is
    /// found on disk through the read's <paramref name="listings"/>, so that a script that includes thousands lists each folder once.
    /// </summary>
    public static void ReadFile(ProjectItem item, string folder, FolderListings listings, SchemaDeclarations into)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(listings);
        ArgumentNullException.ThrowIfNull(into);
        string? problem = null;
        if (item.FullPath is not { } full || DiskPath.Locate(full, item.Spec, listings, out problem) is not { } found)
        {
            Unreadable(item.At, problem ?? $"{item.Type} {item.Spec} isn't found", into);
            return;
        }

        ReadIncluding(found, SqlcmdVariables.Values(), [found], folder, listings, into);
    }

    /// <summary>
    /// One file of a project's deployment script, starting with the variables in <paramref name="values"/>, which it changes as
    /// sqlcmd would. Each file it includes is read after it, with the variables set at its <c>:r</c>; a file already being
    /// included (<paramref name="including"/>) isn't read again. Each is found through <paramref name="listings"/>.
    /// </summary>
    private static void ReadIncluding(string path, Dictionary<string, string> values, ImmutableHashSet<string> including, string folder, FolderListings listings, SchemaDeclarations into)
    {
        var relative = Path.GetRelativePath(folder, path).Replace('\\', '/');
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Unreadable(new SourceLocation(relative, 1, 1), $"{relative} can't be read: {ScriptFile.Why(e)}", into);
            return;
        }

        var includes = SqlcmdIncludes.Commands(text).ToDictionary(c => c.Start);
        var later = new List<(string File, Dictionary<string, string> Values)>();
        var sent = SqlcmdVariables.Substitute(text, values, start =>
        {
            foreach (var command in includes.Where(c => c.Key >= start && c.Key < LineEnd(text, start)).Select(c => c.Value))
            {
                var line = text.AsSpan(0, command.Start).Count('\n') + 1;
                var at = new SourceLocation(relative, line, command.Start - (text.LastIndexOf('\n', Math.Max(command.Start - 1, 0)) + 1) + 1);
                var written = SqlcmdVariables.Replaced(command.Path, values);
                if (written.Contains("$(", StringComparison.Ordinal))
                {
                    Unreadable(at, $"{relative} includes {command.Path} through a SQLCMD variable it doesn't set, whose value is the deployment's", into);
                    continue;
                }

                var included = DiskPath.Full(Path.GetDirectoryName(path) ?? ".", written);
                if (DiskPath.Locate(included, written, listings, out var missing) is not { } file)
                {
                    Unreadable(at, $"{relative} includes {written}, which {missing ?? "isn't there"}", into);
                    continue;
                }

                if (including.Contains(file))
                    continue;
                later.Add((file, new Dictionary<string, string>(values, values.Comparer)));
                Advance(file, values, including.Add(file), listings);
            }
        }, Unset);
        Sent(SqlcmdIncludes.Without(sent), relative, into);
        foreach (var (file, set) in later)
            ReadIncluding(file, set, including.Add(file), folder, listings, into);
    }

    /// <summary>
    /// The variables as they are after an included file and what it includes have run, without reading what they do: the file is
    /// read for that after its including script (<see cref="ReadIncluding"/>), which reports what can't be read. Each is found
    /// through <paramref name="listings"/>.
    /// </summary>
    private static void Advance(string path, Dictionary<string, string> values, ImmutableHashSet<string> including, FolderListings listings)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return;
        }

        var includes = SqlcmdIncludes.Commands(text).ToDictionary(c => c.Start);
        SqlcmdVariables.Substitute(text, values, start =>
        {
            foreach (var command in includes.Where(c => c.Key >= start && c.Key < LineEnd(text, start)).Select(c => c.Value))
            {
                var written = SqlcmdVariables.Replaced(command.Path, values);
                if (!written.Contains("$(", StringComparison.Ordinal)
                    && DiskPath.Locate(DiskPath.Full(Path.GetDirectoryName(path) ?? ".", written), written, listings, out _) is { } file
                    && !including.Contains(file))
                {
                    Advance(file, values, including.Add(file), listings);
                }
            }
        }, null);
    }

    private static int LineEnd(string text, int start) => text.IndexOf('\n', start) is var end and >= 0 ? end : text.Length;

    /// <summary>
    /// A deployment script as the server would receive it, its variables replaced and its includes taken out, read into
    /// <paramref name="into"/>: what it changes, or, when it doesn't parse or changes an object it names through a variable it
    /// doesn't set, the doubt of every object it may name.
    /// </summary>
    private static void Sent(string sent, string file, SchemaDeclarations into)
    {
        var parser = new TSql170Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader(sent);
        if (parser.Parse(reader, out var errors) is TSqlScript script && errors.Count == 0)
        {
            var before = Counts.Of(into);
            WeakeningStatements.Read(script, file, into, deployment: true);
            if (ThroughVariable(into, before) is { } at)
                NotRead(at, $"{file} changes an object it names through a SQLCMD variable it doesn't set, whose value is the deployment's", null, into);
            return;
        }

        var first = errors[0];
        var identifiers = Identifiers(parser, sent);
        NotRead(
            new SourceLocation(file, first.Line, first.Column),
            $"{file} doesn't parse ({first.Message})",
            identifiers.Any(Unnamed) ? null : identifiers,
            into);
    }

    private static bool Unnamed(string name) => name.Contains(Unset, StringComparison.OrdinalIgnoreCase);

    private static bool Unnamed(ObjectName name) => Unnamed(name.Schema) || Unnamed(name.Name) || (name.Database is { } database && Unnamed(database));

    /// <summary>How many of each thing a deployment script's statements record there were before it was read.</summary>
    /// <remarks>A class, not a record: it is never compared, and its sets wouldn't be by value.</remarks>
    private sealed class Counts(int enforcement, int keyDisables, int periodDrops, int triggers, IReadOnlySet<ObjectName> tableChanges, IReadOnlySet<ObjectName> deployed)
    {
        public int Enforcement { get; } = enforcement;

        public int KeyDisables { get; } = keyDisables;

        public int PeriodDrops { get; } = periodDrops;

        public int Triggers { get; } = triggers;

        public IReadOnlySet<ObjectName> TableChanges { get; } = tableChanges;

        public IReadOnlySet<ObjectName> Deployed { get; } = deployed;

        public static Counts Of(SchemaDeclarations into) =>
            new(into.Enforcement.Count, into.KeyDisables.Count, into.PeriodDrops.Count, into.Triggers.Count, into.TableChanges.Keys.ToHashSet(), into.Deployed.Keys.ToHashSet());
    }

    /// <summary>
    /// Where the first statement the script's reading recorded names its object, constraint or key through a variable it doesn't
    /// set (<see cref="Unset"/>), or null when none does. Such a statement may change any object, so it is no change to the one
    /// its text names.
    /// </summary>
    private static SourceLocation? ThroughVariable(SchemaDeclarations into, Counts before) =>
        new[]
        {
            into.Enforcement.Skip(before.Enforcement).Where(e => Unnamed(e.Table) || (e.Constraints?.Any(Unnamed) ?? false)).Select(e => (SourceLocation?)e.At).FirstOrDefault(),
            into.KeyDisables.Skip(before.KeyDisables).Where(k => Unnamed(k.Table) || (k.Key is { } key && Unnamed(key))).Select(k => (SourceLocation?)k.At).FirstOrDefault(),
            into.PeriodDrops.Skip(before.PeriodDrops).Where(r => Unnamed(r.Name)).Select(r => (SourceLocation?)r.At).FirstOrDefault(),
            into.Triggers.Skip(before.Triggers).Where(t => Unnamed(t.Name) || Unnamed(t.Parent)).Select(t => (SourceLocation?)new SourceLocation(t.Span.File, t.Span.Line, t.Span.Column)).FirstOrDefault(),
            into.TableChanges.Where(c => !before.TableChanges.Contains(c.Key) && Unnamed(c.Key)).Select(c => (SourceLocation?)c.Value.At).FirstOrDefault(),
            into.Deployed.Where(c => !before.Deployed.Contains(c.Key) && Unnamed(c.Key)).Select(c => (SourceLocation?)c.Value.At).FirstOrDefault(),
        }.OfType<SourceLocation>().OrderBy(at => at.Line).ThenBy(at => at.Column).FirstOrDefault();

    /// <summary>
    /// A deployment script, or a file it includes, that can't be read at all: reported, with every object of the model doubted,
    /// since not even the names in it are known.
    /// </summary>
    public static void Unreadable(SourceLocation at, string why, SchemaDeclarations into)
    {
        ArgumentNullException.ThrowIfNull(into);
        NotRead(at, why, null, into);
    }

    private static void NotRead(SourceLocation at, string why, IReadOnlySet<string>? identifiers, SchemaDeclarations into)
    {
        var doubted = identifiers is null ? "every table, procedure, function and view is" : "every table, procedure, function and view it names is";
        into.Diagnostics.Add(SourceCodes.DeploymentScriptNotRead.At(at, $"A deployment script can't be read: {why}. What it does isn't known, so {doubted} doubted."));
        into.UnreadDeployments.Add(new UnreadDeployment(identifiers, at));
    }

    /// <summary>Every name in the script's tokens, without its brackets or quotation marks.</summary>
    private static HashSet<string> Identifiers(TSql170Parser parser, string text)
    {
        using var reader = new StringReader(text);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in parser.GetTokenStream(reader, out _))
        {
            if (token.TokenType == TSqlTokenType.Identifier)
                names.Add(token.Text);
            else if (token.TokenType == TSqlTokenType.QuotedIdentifier && token.Text.Length >= 2)
                names.Add(token.Text[0] == '[' ? token.Text[1..^1].Replace("]]", "]", StringComparison.Ordinal) : token.Text[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal));
        }

        return names;
    }
}
