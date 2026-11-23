using Haruspex.Diagnostics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Sources;

/// <summary>
/// Parses a script as the server parses it as sqlcmd sends it, batch by batch, each with the <c>QUOTED_IDENTIFIER</c> in force where
/// it starts, which decides whether <c>"x"</c> is a name or a string.
/// </summary>
/// <remarks>
/// <para>
/// <b>What the server does</b> (observed: <c>A_quoted_identifier_set_outside_a_body_takes_effect_where_it_is_parsed</c>). A
/// <c>SET QUOTED_IDENTIFIER</c> takes effect where it is parsed, not where it runs: from there on in its own batch and in every
/// batch after, under an <c>IF</c> that is false too. <c>SET ANSI_DEFAULTS</c> sets it as well. In a procedure's or trigger's
/// body it does nothing: the body is parsed with the setting in force where it is created, and the session keeps it. (A function
/// or a view can't hold a <c>SET</c>.)
/// </para>
/// <para>
/// <b>What ScriptDom does.</b> It follows a <c>SET QUOTED_IDENTIFIER</c> to the end of its batch, but starts each batch again
/// with the setting it was given, doesn't follow <c>SET ANSI_DEFAULTS</c>, and follows a <c>SET</c> in a body as if it were
/// outside one. So each batch is parsed with the setting the server has where it starts, worked out here from the script's
/// tokens. A run of batches that start with the same setting is parsed in one go, from the script with every character outside
/// the run turned into a space and line breaks kept, so that every line, column and offset is the script's own and a batch
/// that needs another setting doesn't fail the parse. Where ScriptDom would still differ from the server within a batch (a
/// <c>"…"</c> after a <c>SET ANSI_DEFAULTS</c> that changed the setting, or after a <c>SET</c> in a body), the script isn't
/// read, with a parse error that says why.
/// </para>
/// </remarks>
internal static class ScriptParser
{
    /// <summary>
    /// The script parsed batch by batch as the server parses it; null when it doesn't parse, with each error added to
    /// <paramref name="diagnostics"/>.
    /// </summary>
    /// <param name="text">The script's text, as the server receives it.</param>
    /// <param name="file">Its path, which every location names.</param>
    /// <param name="quotedIdentifier">Whether <c>QUOTED_IDENTIFIER</c> is on where it starts.</param>
    /// <param name="diagnostics">Where each error is added.</param>
    public static TSqlScript? Parse(string text, string file, bool quotedIdentifier, ICollection<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(diagnostics);

        // Each "…" is one token whichever the setting, so one pass over the tokens finds where each batch starts and the setting
        // there. A text that doesn't even split into tokens fails as one parse, whose errors say where.
        var tokens = new TSql170Parser(quotedIdentifier).GetTokenStream(new StringReader(text), out var tokenErrors);
        if (tokenErrors.Count > 0)
            return Parsed(text, file, quotedIdentifier, diagnostics);

        var runs = new List<(int Start, int End, bool QuotedIdentifier)>();
        var setting = quotedIdentifier;
        foreach (var (start, end, batch) in Batches(tokens, text.Length))
        {
            if (Followed(batch, setting, file, diagnostics) is not { } after)
                return null;
            if (runs.Count > 0 && runs[^1].QuotedIdentifier == setting)
                runs[^1] = runs[^1] with { End = end };
            else
                runs.Add((start, end, setting));
            setting = after;
        }

        if (runs.Count <= 1)
            return Parsed(text, file, quotedIdentifier, diagnostics);

        var script = new TSqlScript();
        var parsed = true;
        foreach (var (start, end, quoted) in runs)
        {
            if (Parsed(Only(text, start, end), file, quoted, diagnostics) is { } parsedRun)
            {
                foreach (var batch in parsedRun.Batches)
                    script.Batches.Add(batch);
            }
            else
            {
                parsed = false;
            }
        }

        return parsed ? script : null;
    }

    /// <summary>The text parsed whole with one setting, as ScriptDom parses it; null with its errors when it doesn't parse.</summary>
    private static TSqlScript? Parsed(string text, string file, bool quotedIdentifier, ICollection<Diagnostic> diagnostics)
    {
        var fragment = new TSql170Parser(quotedIdentifier).Parse(new StringReader(text), out var errors);

        // A parse error is a position the parser gives, not a fragment: the one place a location is made from a line and column.
        foreach (var error in errors)
            diagnostics.Add(new Diagnostic(SourceCodes.ParseError, error.Message, new SourceLocation(file, error.Line, error.Column)));
        return errors.Count == 0 ? fragment as TSqlScript : null;
    }

    /// <summary>The text with every character outside [<paramref name="start"/>, <paramref name="end"/>) but white space a space.</summary>
    private static string Only(string text, int start, int end) =>
        string.Create(text.Length, (text, start, end), static (span, state) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                var c = state.text[i];
                span[i] = (i >= state.start && i < state.end) || char.IsWhiteSpace(c) ? c : ' ';
            }
        });

    /// <summary>
    /// The batches of a script as sqlcmd splits it at <c>GO</c>: where each starts and ends in the text, and its tokens. What
    /// follows <c>GO</c> on its line (a count) is part of the separator.
    /// </summary>
    private static IEnumerable<(int Start, int End, List<TSqlParserToken> Tokens)> Batches(IList<TSqlParserToken> tokens, int length)
    {
        var start = 0;
        var batch = new List<TSqlParserToken>();
        var separatorLine = -1;
        foreach (var token in tokens)
        {
            if (token.TokenType == TSqlTokenType.EndOfFile)
            {
                break;
            }
            else if (token.TokenType == TSqlTokenType.Go)
            {
                yield return (start, token.Offset, batch);
                start = token.Offset + token.Text.Length;
                separatorLine = token.Line;
                batch = [];
            }
            else if (token.Line == separatorLine && token.TokenType != TSqlTokenType.WhiteSpace)
            {
                start = token.Offset + token.Text.Length;
            }
            else
            {
                batch.Add(token);
            }
        }

        yield return (start, length, batch);
    }

    /// <summary>
    /// The setting the server has after a batch that starts with <paramref name="setting"/>; null, with a parse error, when ScriptDom
    /// would read a <c>"…"</c> in it otherwise than the server does.
    /// </summary>
    private static bool? Followed(List<TSqlParserToken> batch, bool setting, string file, ICollection<Diagnostic> diagnostics)
    {
        var body = IsBody(batch);
        var server = setting;
        var scriptDom = setting;
        for (var i = 0; i < batch.Count; i++)
        {
            var token = batch[i];
            if (Set(batch, i) is ({ } options, var on))
            {
                if (options.Contains("QUOTED_IDENTIFIER"))
                    scriptDom = on;
                if (!body && (options.Contains("QUOTED_IDENTIFIER") || options.Contains("ANSI_DEFAULTS")))
                    server = on;
            }
            else if (token.TokenType == TSqlTokenType.AsciiStringOrQuotedIdentifier && scriptDom != server)
            {
                var why = body ? "a SET in a module's body, which the server ignores and its parser doesn't" : "SET ANSI_DEFAULTS, which its parser doesn't follow";
                diagnostics.Add(new Diagnostic(
                    SourceCodes.ParseError,
                    $"The server reads {token.Text} as {(server ? "a name" : "a string")} where haruspex's parser would read it as {(server ? "a string" : "a name")}, after {why}; so the script isn't read.",
                    new SourceLocation(file, token.Line, token.Column)));
                return null;
            }
        }

        return server;
    }

    /// <summary>
    /// The options a <c>SET</c> at <paramref name="at"/> names, upper case, and whether it turns them on; null when the token there
    /// doesn't start a <c>SET</c> of options to <c>ON</c> or <c>OFF</c>. <c>ALTER DATABASE … SET QUOTED_IDENTIFIER OFF</c> is no
    /// such <c>SET</c>: it changes the database's default, which every connection that sets the option overrides, as sqlcmd and the
    /// client libraries do when they connect (observed with sqlcmd: the next batch and a procedure created in it read <c>"x"</c>
    /// as a name).
    /// </summary>
    private static (HashSet<string>? Options, bool On) Set(List<TSqlParserToken> batch, int at)
    {
        if (batch[at].TokenType != TSqlTokenType.Set || AltersADatabase(batch, at))
            return (null, false);
        var options = new HashSet<string>(StringComparer.Ordinal);
        var expectOption = true;
        foreach (var token in Significant(batch, at + 1))
        {
            switch (token.TokenType)
            {
                case TSqlTokenType.Identifier when expectOption:
                    options.Add(token.Text.ToUpperInvariant());
                    expectOption = false;
                    break;
                case TSqlTokenType.Comma when !expectOption:
                    expectOption = true;
                    break;
                case TSqlTokenType.On or TSqlTokenType.Off when !expectOption:
                    return (options, token.TokenType == TSqlTokenType.On);
                default:
                    return (null, false);
            }
        }

        return (null, false);
    }

    /// <summary>Whether the <c>SET</c> at <paramref name="at"/> follows <c>ALTER DATABASE</c> and the database's name.</summary>
    private static bool AltersADatabase(List<TSqlParserToken> batch, int at)
    {
        var before = batch.Take(at).Reverse().Where(t => t.TokenType is not (TSqlTokenType.WhiteSpace or TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment))
            .Take(3).Select(t => t.TokenType).ToList();
        return before is [_, TSqlTokenType.Database, TSqlTokenType.Alter];
    }

    /// <summary>
    /// Whether the batch is a procedure's, function's or trigger's definition, whose body runs to the batch's end: it starts with
    /// <c>CREATE</c>, <c>CREATE OR ALTER</c> or <c>ALTER</c> of one.
    /// </summary>
    private static bool IsBody(List<TSqlParserToken> batch)
    {
        var first = Significant(batch, 0).Take(4).Select(t => t.TokenType).ToList();
        var kind = first switch
        {
            [TSqlTokenType.Create, TSqlTokenType.Or, TSqlTokenType.Alter, var k, ..] => k,
            [TSqlTokenType.Create or TSqlTokenType.Alter, var k, ..] => k,
            _ => TSqlTokenType.None,
        };
        return kind is TSqlTokenType.Proc or TSqlTokenType.Procedure or TSqlTokenType.Function or TSqlTokenType.Trigger;
    }

    /// <summary>The tokens from <paramref name="from"/> on that aren't white space or a comment.</summary>
    private static IEnumerable<TSqlParserToken> Significant(List<TSqlParserToken> batch, int from) =>
        batch.Skip(from).Where(t => t.TokenType is not (TSqlTokenType.WhiteSpace or TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment));
}
