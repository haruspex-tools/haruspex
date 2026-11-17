using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Haruspex.Config;

/// <summary>
/// The naming <c>replace</c> rule that would tell two clashing names of one schema apart, built from where they differ, or
/// why no single rule can (HAR4303). A suggestion is only made once it has been checked: the rules, with it added after them,
/// give the two names different results.
/// </summary>
/// <remarks>
/// <para>A case style makes two names the same by dropping what is not a letter or digit: <c>order$lines</c> and
/// <c>order#lines</c> are both <c>OrderLines</c>. So the rule replaces the characters on one side of the difference with a word
/// (<c>{ "pattern": "#", "with": "_hash_" }</c>), which the case style keeps, and the reader puts a word of their own in its
/// place. The difference is taken as the text after the rules already written have run, since that is what a rule added
/// after them sees, and widened to the whole run of such characters around it, so that <c>__</c> against <c>_</c> is a rule
/// for <c>__</c>, not for every <c>_</c>.</para>
/// <para>No rule is suggested where the difference holds a letter or a digit (a rule for it would rewrite words in every
/// other name), or where the names are already the same text, since nothing a later rule matches tells them apart.</para>
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
    /// <param name="Where">Where the two names differ, in words: <c>("#" against "$")</c>.</param>
    /// <param name="Word">What the rule puts in, for the reader to make their own: <c>hash</c>.</param>
    public sealed record Suggestion(string Rule, string Where, string Word);

    /// <summary>
    /// The rule that tells <paramref name="first"/> from <paramref name="second"/> under <paramref name="rules"/>; or null,
    /// and why not.
    /// </summary>
    public static Suggestion? For(NamingRules rules, ReservedWords reserved, string first, string second, out string? whyNot)
    {
        var a = rules.Replaced(first);
        var b = rules.Replaced(second);
        if (a is null || b is null)
        {
            whyNot = "a \"replace\" rule took too long on them";
            return null;
        }

        if (a == b)
        {
            whyNot = $"their names are both {Quoted(a)} once the \"replace\" rules have run, so no further rule tells them apart";
            return null;
        }

        var (ownA, ownB) = Differing(a, b);
        var where = $"({Described(ownA)} against {Described(ownB)})";

        // Each side's run is a candidate: one a pattern can name without escaping first, then the first name's.
        foreach (var run in new[] { ownA, ownB }.Where(r => r.Length > 0 && r.All(c => !char.IsAsciiLetterOrDigit(c))).OrderBy(r => Pattern(r) == r ? 0 : 1))
        {
            var with = "_" + string.Join("_", run.Select(Word)) + "_";
            var rule = new NamingReplacement(new Regex(Pattern(run), RegexOptions.CultureInvariant | RegexOptions.NonBacktracking), with, null);
            var added = rules with { Replace = [.. rules.Replace, rule] };
            if (added.Name(first, reserved, out _) is { } one && added.Name(second, reserved, out _) is { } other && one != other)
            {
                whyNot = null;
                return new Suggestion($"{{ \"pattern\": {Json(Pattern(run))}, \"with\": {Json(with)} }}", where, with.Trim('_'));
            }
        }

        whyNot = $"no \"replace\" rule tells {Quoted(a)} from {Quoted(b)} without rewriting letters or digits";
        return null;
    }

    /// <summary>
    /// Where two texts differ: after their common start and before their common end, widened to the run of characters that
    /// are not letters or digits around it.
    /// </summary>
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

    /// <summary>A literal as a pattern: escaped where it must be, but <c>#</c> and a space as they are, which need it only under options a rule never has.</summary>
    private static string Pattern(string literal) =>
        Regex.Escape(literal).Replace("\\#", "#", StringComparison.Ordinal).Replace("\\ ", " ", StringComparison.Ordinal);

    private static string Described(string text) => text.Length == 0 ? "nothing" : Quoted(text);

    private static string Quoted(string text) => "\"" + text + "\"";

    private static string Json(string text) => JsonSerializer.Serialize(text, JsonOptions);

    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
}
