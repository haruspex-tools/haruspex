using System.Text;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Semantics;

/// <summary>Source text of fragments, for messages.</summary>
public static class FragmentText
{
    /// <summary>The fragment's source text on one line, comments and runs of white space as one space, shortened to 80 characters.</summary>
    public static string OneLine(TSqlFragment fragment)
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

        var result = text.ToString().Trim();
        return result.Length <= 80 ? result : result[..77] + "...";
    }
}
