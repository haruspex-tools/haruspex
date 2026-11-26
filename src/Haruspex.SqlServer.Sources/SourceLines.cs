namespace Haruspex.SqlServer.Sources;

/// <summary>
/// A text's lines as a location counts them: split at CR, LF and CRLF alone, as the parser counts lines, so that line
/// <c>n</c> here is the line a location's <c>n</c> means. .NET's own <c>ReplaceLineEndings</c> also breaks at U+2028, U+2029, NEL
/// and form feed, which the parser leaves inside a line, so every line after one of them would be off.
/// </summary>
public static class SourceLines
{
    /// <summary>The lines of <paramref name="text"/>, without their endings.</summary>
    public static IReadOnlyList<string> Of(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('\r' or '\n'))
                continue;
            lines.Add(text[start..i]);
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                i++;
            start = i + 1;
        }

        lines.Add(text[start..]);
        return lines;
    }

    /// <summary>
    /// <paramref name="text"/> with each CR and CRLF made LF and nothing else changed: the lines of <see cref="Of"/> joined by LF,
    /// where <c>ReplaceLineEndings("\n")</c> would change a form feed, NEL, U+2028 and U+2029 too.
    /// </summary>
    public static string WithLineFeeds(string text) => string.Join('\n', Of(text));
}
