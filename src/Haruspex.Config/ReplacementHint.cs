using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Haruspex.Config;

/// <summary>
/// The naming <c>replace</c> rule that would part procedures of one schema whose names clash, built from their names, or why no
/// single rule can (HAR4303). A rule is only suggested once it has been checked against every procedure the schema's rules name:
/// with it added after them, the clashing names all differ, and no name, theirs or another's, comes out the same as one it
/// didn't before.
/// </summary>
/// <remarks>
/// <para>A case style makes two names the same by dropping what is not a letter or digit: <c>order$lines</c> and
/// <c>order#lines</c> are both <c>OrderLines</c>. So the rule replaces a run of such characters with a word
/// (<c>{ "pattern": "#", "with": "_hash_" }</c>), which the case style keeps, and the reader puts a word of their own in its
/// place. Runs are taken from the text after the rules already written have run, since that is what a rule added after them
/// sees, each whole, so that <c>__</c> against <c>_</c> is a rule for <c>__</c>, not for every <c>_</c>.</para>
/// <para>The candidates are tried in order: runs some of the clashing names have and others don't, since those are what tell
/// them apart, before runs all of them have (<c>x$y#z</c> against <c>x#y$z</c>, where a rule for either still parts them); then
/// a pattern that needs no escaping, then the longer, then by character. Each is tried with its word, and then with the word
/// numbered, should the word itself make another name.</para>
/// <para>No rule is suggested where the names are already the same text (nothing a later rule matches tells them apart),
/// where they differ only in letters or digits (a rule for that would rewrite words in every other name), or where no candidate
/// passes the check. Type names, built on these, aren't checked: a clash among them is reported in its turn.</para>
/// </remarks>
internal static class ReplacementHint
{
    /// <summary>What a rule for a character puts in its place: a word the case style keeps, for the reader to change.</summary>
    private static string Word(char c) => c switch
    {
        ' ' => "space",
        '!' => "bang",
        '#' => "hash",
        '$' => "dollar",
        '%' => "percent",
        '&' => "and",
        '*' => "star",
        '+' => "plus",
        '-' => "dash",
        '.' => "dot",
        '/' => "slash",
        ':' => "colon",
        '=' => "equals",
        '?' => "query",
        '@' => "at",
        '^' => "caret",
        '_' => "underscore",
        '|' => "bar",
        '~' => "tilde",
        _ => "u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture),
    };

    /// <summary>A rule to suggest.</summary>
    /// <param name="Rule">As it goes in the config: <c>{ "pattern": "#", "with": "_hash_" }</c>.</param>
    /// <param name="Where">What it is for, in words: <c>where their names differ ("#" against "$")</c>, or <c>the "#" in their names</c>.</param>
    /// <param name="Word">What the rule puts in, for the reader to make their own: <c>hash</c>.</param>
    public sealed record Suggestion(string Rule, string Where, string Word);

    /// <summary>The rule that parts the procedures named <paramref name="clashing"/>, or null, and why not.</summary>
    /// <param name="rules">The schema's naming rules.</param>
    /// <param name="reserved">The words the generated code's language keeps.</param>
    /// <param name="clashing">The names, within the schema, of the procedures the rules give one name.</param>
    /// <param name="others">The names of the schema's other procedures the rules name, which a rule renames too.</param>
    /// <param name="taken">The names held by everything else: overrides, other schemas' procedures and query files.</param>
    /// <param name="whyNot">Why there is no rule, when there isn't.</param>
    public static Suggestion? For(
        NamingRules rules, ReservedWords reserved, IReadOnlyList<string> clashing, IReadOnlyList<string> others, IReadOnlySet<string> taken, out string? whyNot)
    {
        var replaced = clashing.Select(rules.Replaced).ToList();
        if (replaced.Any(t => t is null))
        {
            whyNot = "a \"replace\" rule took too long on them";
            return null;
        }

        var texts = replaced.OfType<string>().ToList();
        if (texts.GroupBy(t => t).FirstOrDefault(g => g.Count() > 1) is { } same)
        {
            whyNot = $"{(texts.Count == 2 ? "their names are" : "two of their names are")} both {Quoted(same.Key)} once the \"replace\" rules have run, so no further rule tells them apart";
            return null;
        }

        var runs = texts.Select(Runs).ToList();
        var candidates = runs.SelectMany(r => r).Distinct(StringComparer.Ordinal)
            .OrderBy(run => runs.All(r => r.Contains(run)) ? 1 : 0)
            .ThenBy(run => Pattern(run) == run ? 0 : 1)
            .ThenByDescending(run => run.Length)
            .ThenBy(run => run, StringComparer.Ordinal)
            .ToList();
        if (candidates.Count == 0)
        {
            whyNot = $"no \"replace\" rule tells {Listed(texts)} without rewriting letters or digits";
            return null;
        }

        var names = clashing.Concat(others).ToList();
        var before = names.Select(n => rules.Name(n, reserved, out _)).ToList();
        foreach (var run in candidates)
        {
            var word = string.Join("_", run.Select(Word));
            foreach (var numbered in new[] { word, word + "2", word + "3" })
            {
                var with = "_" + numbered + "_";
                var added = rules with { Replace = [.. rules.Replace, new NamingReplacement(new Regex(Pattern(run), RegexOptions.CultureInvariant | RegexOptions.NonBacktracking), with, null)] };
                if (Parts(added, reserved, names, before, clashing.Count, taken))
                {
                    whyNot = null;
                    var where = texts.Count == 2 && Differing(texts[0], texts[1]) is var (a, b) && (a + b).All(c => !char.IsAsciiLetterOrDigit(c))
                        ? $"where their names differ ({Described(a)} against {Described(b)})"
                        : $"the {Quoted(run)} in their names";
                    return new Suggestion($"{{ \"pattern\": {Json(Pattern(run))}, \"with\": {Json(with)} }}", where, numbered);
                }
            }
        }

        whyNot = $"no single \"replace\" rule tells {Listed(texts)} without another name clashing";
        return null;
    }

    /// <summary>
    /// Whether <paramref name="rules"/> name every one of <paramref name="names"/>, give the first <paramref name="clashing"/>
    /// names that no other name has, and give no two names, nor a name and one <paramref name="taken"/>, the same result where
    /// <paramref name="before"/> didn't.
    /// </summary>
    private static bool Parts(NamingRules rules, ReservedWords reserved, List<string> names, List<string?> before, int clashing, IReadOnlySet<string> taken)
    {
        var named = names.Select(n => rules.Name(n, reserved, out _)).ToList();
        if (named.Any(n => n is null))
            return false;
        var after = named.OfType<string>().ToList();
        for (var i = 0; i < names.Count; i++)
        {
            if (taken.Contains(after[i]) && (i < clashing || !taken.Contains(before[i] ?? "")))
                return false;
            for (var j = i + 1; j < names.Count; j++)
            {
                if (after[i] == after[j] && (i < clashing || before[i] != before[j]))
                    return false;
            }
        }

        return true;
    }

    /// <summary>Each run of characters that are not letters or digits in <paramref name="text"/>, whole.</summary>
    private static HashSet<string> Runs(string text) =>
        [.. Regex.Matches(text, "[^A-Za-z0-9]+", RegexOptions.CultureInvariant).Select(m => m.Value)];

    /// <summary>Two texts' parts after their common start and before their common end, widened to the runs around them.</summary>
    private static (string A, string B) Differing(string a, string b)
    {
        var start = 0;
        while (start < a.Length && start < b.Length && a[start] == b[start])
            start++;
        var end = 0;
        while (end < a.Length - start && end < b.Length - start && a[^(end + 1)] == b[^(end + 1)])
            end++;
        while (start > 0 && !char.IsAsciiLetterOrDigit(a[start - 1]))
            start--;
        while (end > 0 && !char.IsAsciiLetterOrDigit(a[^end]))
            end--;
        return (a[start..^end], b[start..^end]);
    }

    /// <summary>Names as a message lists them: <c>"a" from "b"</c>, or <c>"a", "b" and "c" apart</c>.</summary>
    private static string Listed(List<string> texts) =>
        texts.Count == 2 ? $"{Quoted(texts[0])} from {Quoted(texts[1])}"
        : string.Join(", ", texts.Take(texts.Count - 1).Select(Quoted)) + " and " + Quoted(texts[^1]) + " apart";

    /// <summary>A literal as a pattern: escaped where it must be, but <c>#</c> and a space as they are, which need it only under options a rule never has.</summary>
    private static string Pattern(string literal) =>
        Regex.Escape(literal).Replace("\\#", "#", StringComparison.Ordinal).Replace("\\ ", " ", StringComparison.Ordinal);

    private static string Described(string text) => text.Length == 0 ? "nothing" : Quoted(text);

    private static string Quoted(string text) => "\"" + text + "\"";

    /// <summary>
    /// <paramref name="text"/> as a JSON string, to copy into the config file: escaped as JSON needs, and other characters as they
    /// are (the relaxed encoder), as <c>JsonSerializer</c> wrote it with that encoder. <c>JsonEncodedText</c> needs no reflection,
    /// which <c>JsonSerializer</c> does without a source-generated context, and Native AOT refuses (NativeAotTests).
    /// </summary>
    internal static string Json(string text) => $"\"{JsonEncodedText.Encode(text, JavaScriptEncoder.UnsafeRelaxedJsonEscaping)}\"";
}
