using System.Text;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Syntax;

/// <summary>Source text of fragments, for messages, and to compare one condition read from two sources.</summary>
public static class FragmentText
{
    /// <summary>
    /// The fragment's whole source text on one line, comments and runs of white space as one space, so that one text laid out on
    /// several lines, or held by another source (a folder's script, a DACPAC's copy of it), reads alike. Not ScriptDom's
    /// generator, which would rewrite it more fully but isn't safe to compile ahead of time.
    /// </summary>
    public static string Normalised(TSqlFragment fragment) => Joined(fragment);

    /// <summary>The fragment's source text on one line, comments and runs of white space as one space, shortened to 80 characters.</summary>
    public static string OneLine(TSqlFragment fragment)
    {
        var result = Joined(fragment);
        return result.Length <= 80 ? result : result[..77] + "...";
    }

    private static string Joined(TSqlFragment fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        var text = new StringBuilder();
        for (var i = fragment.FirstTokenIndex; i <= fragment.LastTokenIndex; i++)
        {
            var token = fragment.ScriptTokenStream[i];
            if (token.TokenType is TSqlTokenType.WhiteSpace or TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment)
            {
                if (text.Length > 0 && text[^1] != ' ')
                    text.Append(' ');
                continue;
            }

            text.Append(token.Text);
        }

        return text.ToString().Trim();
    }
}
