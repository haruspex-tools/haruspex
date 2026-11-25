using System.Text;
using Haruspex.Diagnostics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Sources;

/// <summary>
/// Which scripts hold queries alone, and so nothing a database's scripts are read for: the test <c>haruspex init</c> uses to keep
/// an application's query files out of the folder it reads as the database.
/// </summary>
public static class QueryOnlyScripts
{
    /// <summary>
    /// Whether the script at <paramref name="path"/> parses and every statement in it is a <c>SELECT</c> that writes nowhere (no
    /// <c>INTO</c>), or it holds none.
    /// </summary>
    /// <remarks>
    /// Such a script leaves the model as it is: <c>ScriptReader</c> passes a query over. Anything else (a definition, an
    /// <c>ALTER</c>, a <c>NOCHECK</c>, seed data, a statement that doesn't parse) may change what is claimed, so it counts as the
    /// database's, and leaving it out of the source could claim more than the deployed database holds. Read as a script is
    /// (<c>ScriptFile</c>): UTF-8 unless a byte order mark says otherwise, <c>QUOTED_IDENTIFIER</c> on where it starts.
    /// </remarks>
    public static bool Is(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string text;
        try
        {
            using var file = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            text = file.ReadToEnd();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        var problems = new List<Diagnostic>();
        return ScriptParser.Parse(text, path, quotedIdentifier: true, problems) is { } script
            && problems.Count == 0
            && script.Batches.SelectMany(b => b.Statements).All(s => s is SelectStatement { Into: null });
    }
}
