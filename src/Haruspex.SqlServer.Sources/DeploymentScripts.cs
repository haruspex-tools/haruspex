using Haruspex.Diagnostics;
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
/// or read, is a warning (HAR1021), and every object whose name it holds is doubted (<see cref="UnreadDeployment"/>).
/// </para>
/// </remarks>
internal static class DeploymentScripts
{
    /// <summary>Reads one deployment script's text into <paramref name="into"/>.</summary>
    /// <param name="text">The script, as it is in its file or its package.</param>
    /// <param name="file">Its file, which every location names.</param>
    /// <param name="into">What the source declares.</param>
    public static void Read(string text, string file, SchemaDeclarations into)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(into);
        var sent = SqlcmdVariables.Substitute(SqlcmdIncludes.Without(text));
        var parser = new TSql170Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader(sent);
        if (parser.Parse(reader, out var errors) is TSqlScript script && errors.Count == 0)
        {
            WeakeningStatements.Read(script, file, into, deployment: true);
            return;
        }

        var first = errors[0];
        NotRead(new SourceLocation(file, first.Line, first.Column), $"{file} doesn't parse ({first.Message})", Identifiers(parser, sent), into);
    }

    /// <summary>
    /// A project's deployment script, from its item, with every file it includes by <c>:r</c>, each read apart and named relative
    /// to <paramref name="folder"/>, as the project's scripts are.
    /// </summary>
    public static void ReadFile(ProjectItem item, string folder, SchemaDeclarations into)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(into);
        string? problem = null;
        if (item.FullPath is not { } full || DiskPath.Locate(full, item.Spec, out problem) is not { } found)
        {
            Unreadable(item.At, problem ?? $"{item.Type} {item.Spec} isn't found", into);
            return;
        }

        var pending = new Queue<string>([found]);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (pending.TryDequeue(out var path))
        {
            if (!seen.Add(path))
                continue;
            var relative = Path.GetRelativePath(folder, path).Replace('\\', '/');
            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Unreadable(new SourceLocation(relative, 1, 1), $"{relative} can't be read: {ScriptFile.Why(e)}", into);
                continue;
            }

            Read(text, relative, into);
            foreach (var (start, _, written) in SqlcmdIncludes.Commands(text))
            {
                var line = text.AsSpan(0, start).Count('\n') + 1;
                var at = new SourceLocation(relative, line, start - (text.LastIndexOf('\n', Math.Max(start - 1, 0)) + 1) + 1);
                if (written.Contains("$(", StringComparison.Ordinal))
                {
                    Unreadable(at, $"{relative} includes {written} through a SQLCMD variable, whose value is the deployment's", into);
                    continue;
                }

                var included = DiskPath.Full(Path.GetDirectoryName(path) ?? ".", written);
                if (DiskPath.Locate(included, written, out var missing) is { } file)
                    pending.Enqueue(file);
                else
                    Unreadable(at, $"{relative} includes {written}, which {missing ?? "isn't there"}", into);
            }
        }
    }

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
