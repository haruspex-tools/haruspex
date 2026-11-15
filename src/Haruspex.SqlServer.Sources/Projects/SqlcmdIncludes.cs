namespace Haruspex.SqlServer.Sources.Projects;

/// <summary>
/// The scripts a pre- or post-deployment script includes with sqlcmd's <c>:r</c>, as a <c>Microsoft.Build.Sql</c> build finds them
/// to take them out of <c>Build</c>: a line that starts with <c>:r</c> (any case, after spaces or tabs), and not one inside a
/// <c>/* */</c> comment, after <c>--</c>, or inside a string (observed under 2.3.0: a script named by each of those three is built,
/// one named by a real <c>:r</c> line isn't). The path is the rest of the line, quoted or not.
/// </summary>
/// <remarks>
/// A quoted name (<c>"…"</c>, <c>[…]</c>) is skipped as a string is, so a <c>:r</c> inside one isn't taken for a command. Block
/// comments nest, as T-SQL's do.
/// </remarks>
internal static class SqlcmdIncludes
{
    /// <summary>The paths <paramref name="script"/> includes, as written, in order.</summary>
    public static IEnumerable<string> Of(string script)
    {
        ArgumentNullException.ThrowIfNull(script);
        var depth = 0;
        char? closing = null;
        var lineStart = true;
        for (var i = 0; i < script.Length; i++)
        {
            var c = script[i];
            var next = i + 1 < script.Length ? script[i + 1] : '\0';
            if (closing is { } close)
            {
                // Inside a string or quoted name: a doubled closing character is the character itself.
                if (c == close && next == close)
                    i++;
                else if (c == close)
                    closing = null;
                lineStart = c == '\n';
                continue;
            }

            if (depth > 0)
            {
                if (c == '/' && next == '*')
                {
                    depth++;
                    i++;
                }
                else if (c == '*' && next == '/')
                {
                    depth--;
                    i++;
                }

                lineStart = c == '\n';
                continue;
            }

            switch (c)
            {
                case '\n':
                    lineStart = true;
                    continue;
                case ' ' or '\t' or '\r' when lineStart:
                    continue;
                case ':' when lineStart && next is 'r' or 'R' && i + 2 < script.Length && script[i + 2] is ' ' or '\t':
                    var end = script.IndexOf('\n', i);
                    var line = script[(i + 2)..(end < 0 ? script.Length : end)].Trim();
                    yield return line.Length > 1 && line[0] == '"' && line[^1] == '"' ? line[1..^1] : line;
                    i = end < 0 ? script.Length : end - 1;
                    continue;
                case '-' when next == '-':
                    var eol = script.IndexOf('\n', i);
                    i = eol < 0 ? script.Length : eol - 1;
                    continue;
                case '/' when next == '*':
                    depth = 1;
                    i++;
                    break;
                case '\'':
                    closing = '\'';
                    break;
                case '"':
                    closing = '"';
                    break;
                case '[':
                    closing = ']';
                    break;
            }

            lineStart = false;
        }
    }
}
