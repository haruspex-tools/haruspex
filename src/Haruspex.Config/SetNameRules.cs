namespace Haruspex.Config;

/// <summary>
/// What a result set's name may be, wherever it is given: a procedure's <c>sets</c> in the config file, or a query file's
/// <c>-- @sets</c> comment. Each name is a key of the object the function returns, beside <c>output</c> and <c>returnValue</c>,
/// written in camel case as the function's own keys are, and never a reserved word, so that every module can bind it
/// (<c>docs/decisions/0070-a-procedure-s-result-sets-are-named-all-or-none.md</c>).
/// </summary>
internal static class SetNameRules
{
    /// <summary>
    /// Why <paramref name="name"/> can't name a set after <paramref name="earlier"/>, as a clause that follows the name in quotes;
    /// null when it can.
    /// </summary>
    public static string? Problem(string name, IReadOnlyCollection<string> earlier, ReservedWords reserved)
    {
        if (!IsCamelCase(name) || reserved.Contains(name))
            return "can't name a set: a set's name is a key in camel case, a lower-case ASCII letter then letters and digits, and not a reserved word.";
        if (name is "output" or "returnValue")
            return $"can't name a set: the function already returns its {(name == "output" ? "OUTPUT parameters" : "return code")} under it.";
        return earlier.Contains(name, StringComparer.Ordinal) ? "names two sets; give each its own name." : null;
    }

    /// <summary>Whether <paramref name="name"/> is in camel case: a lower-case ASCII letter, then ASCII letters and digits.</summary>
    private static bool IsCamelCase(string name) =>
        name.Length > 0 && char.IsAsciiLetterLower(name[0]) && name.All(char.IsAsciiLetterOrDigit);
}
