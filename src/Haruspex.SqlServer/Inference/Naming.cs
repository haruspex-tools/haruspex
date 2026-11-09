using System.Text;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// Names for generated code. Provisional: the config file will let users override any name, so these only
/// need to be readable, deterministic and unique.
/// </summary>
internal static class Naming
{
    /// <summary><c>get_order</c> → <c>GetOrder</c>; <c>placedAt</c> → <c>PlacedAt</c>.</summary>
    public static string Pascal(string text)
    {
        var result = new StringBuilder();
        foreach (var word in text.Split(static c => !char.IsAsciiLetterOrDigit(c)))
        {
            if (word.Length > 0)
                result.Append(char.ToUpperInvariant(word[0])).Append(word.AsSpan(1));
        }

        if (result.Length == 0)
            return "Unnamed";
        return char.IsAsciiDigit(result[0]) ? "_" + result : result.ToString();
    }

    /// <summary>The name for one item of a list: <c>Lines</c> → <c>Line</c>, <c>Data</c> → <c>DataItem</c>.</summary>
    public static string Singular(string pascal)
    {
        if (pascal.EndsWith("ies", StringComparison.Ordinal) && pascal.Length > 3)
            return pascal[..^3] + "y";
        if (pascal.EndsWith("sses", StringComparison.Ordinal) || pascal.EndsWith("xes", StringComparison.Ordinal)
            || pascal.EndsWith("ches", StringComparison.Ordinal) || pascal.EndsWith("shes", StringComparison.Ordinal)
            || pascal.EndsWith("uses", StringComparison.Ordinal))
            return pascal[..^2];
        if (pascal.EndsWith('s') && !pascal.EndsWith("ss", StringComparison.Ordinal) && pascal.Length > 1)
            return pascal[..^1];
        return pascal + "Item";
    }

    private static IEnumerable<string> Split(this string text, Func<char, bool> separator)
    {
        var start = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i == text.Length || separator(text[i]))
            {
                yield return text[start..i];
                start = i + 1;
            }
        }
    }
}
