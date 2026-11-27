using System.Text;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// The URL an endpoint is declared for (<c>endpoints[].url</c>), and whether a URL a call sends is one of those it stands for. The
/// scheme and the host compare without case, as the server reads them (observed: The_scheme_and_the_host_are_read_without_case);
/// everything after them compares exactly, the query and a fragment included, which the server sends as written (observed:
/// The_path_the_query_and_a_fragment_are_sent_as_written). A <c>{name}</c> stands for text that is never empty and never holds
/// <c>/</c>, <c>?</c>, <c>#</c>, <c>&amp;</c> or <c>=</c>: a segment of the path, or a value of the query; and is never <c>.</c> or
/// <c>..</c>, a dot segment, which the server removes from a path before sending it (observed:
/// Dot_segments_are_removed_from_the_path_before_it_is_sent), so that <c>/plans/.</c> reaches <c>/plans/</c>. Why:
/// docs/decisions/0140-a-rest-response-is-typed-from-a-schema-declared-for-its-endpoint.md.
/// </summary>
internal sealed class UrlTemplate
{
    /// <summary>The characters a <c>{name}</c> never stands for, which end a segment of the path or a value of the query.</summary>
    private const string Ends = "/?#&=";

    /// <summary>The template's parts: a character it holds, or null for a <c>{name}</c>. Its scheme and host are upper case, so that they compare without case.</summary>
    private readonly char?[] parts;

    private UrlTemplate(string text, char?[] parts)
    {
        Text = text;
        this.parts = parts;
    }

    /// <summary>The URL as the declaration writes it.</summary>
    public string Text { get; }

    /// <summary>
    /// The template <paramref name="url"/> writes; null when it isn't one, with why: it must be an absolute <c>https</c> URL, its host
    /// written out, each <c>{</c> closed by a <c>}</c> around a name, and no two names side by side, which would leave where one ends
    /// unsaid.
    /// </summary>
    public static UrlTemplate? Parse(string url, out string problem)
    {
        ArgumentNullException.ThrowIfNull(url);
        problem = "";
        const string Scheme = "https://";
        if (!url.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase) || url.Length == Scheme.Length || Ends.Contains(url[Scheme.Length], StringComparison.Ordinal))
        {
            problem = $"'{url}' is not an https URL with its host: SQL Server calls only https endpoints (31610).";
            return null;
        }

        var host = HostEnd(url);
        if (url.AsSpan(0, host).ContainsAny('{', '}'))
        {
            problem = $"'{url}' has a {{name}} in its host, which only its path or query may have.";
            return null;
        }

        var parts = new List<char?>(url[..host].ToUpperInvariant().Select(c => (char?)c));
        for (var i = host; i < url.Length; i++)
        {
            switch (url[i])
            {
                case '{':
                    var close = url.IndexOf('}', i);
                    var name = close < 0 ? "" : url[(i + 1)..close];
                    if (name.Length == 0 || !name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
                    {
                        problem = $"'{url}' has a '{{' that doesn't close around a name of letters, digits and underscores.";
                        return null;
                    }

                    if (parts.Count > 0 && parts[^1] is null)
                    {
                        problem = $"'{url}' has two {{names}} side by side, so where one ends is not said.";
                        return null;
                    }

                    parts.Add(null);
                    i = close;
                    break;
                case '}':
                    problem = $"'{url}' has a '}}' without its '{{'.";
                    return null;
                default:
                    parts.Add(url[i]);
                    break;
            }
        }

        return new UrlTemplate(url, [.. parts]);
    }

    /// <summary>
    /// Whether every URL <paramref name="sent"/> may be is one this template stands for: its text matched exactly, and each number
    /// in it inside a <c>{name}</c>, whose digits are not known. Null parts of the scheme and host aren't read.
    /// </summary>
    public bool Matches(SentText sent)
    {
        ArgumentNullException.ThrowIfNull(sent);

        // The sent text as characters, a number as null; its scheme and host, written in literal text, upper case.
        var text = new List<char?>();
        foreach (var part in sent.Parts)
        {
            if (part is SentText.Literal literal)
                text.AddRange(literal.Text.Select(c => (char?)c));
            else
                text.Add(null);
        }

        var prefix = new StringBuilder();
        foreach (var c in text)
        {
            if (c is not { } known)
                break;
            prefix.Append(known);
        }

        var host = HostEnd(prefix.ToString());
        if (host == prefix.Length && prefix.Length < text.Count)
            return false; // A number in the host.
        for (var i = 0; i < host; i++)
            text[i] = char.ToUpperInvariant(prefix[i]);

        var memo = new Dictionary<(int, int), bool>();
        return From(0, 0);

        // Whether the template from part t matches the text from position s.
        bool From(int t, int s)
        {
            if (t == parts.Length)
                return s == text.Count;
            if (memo.TryGetValue((t, s), out var known))
                return known;
            bool matched;
            if (parts[t] is { } c)
            {
                matched = s < text.Count && text[s] == c && From(t + 1, s + 1);
            }
            else
            {
                matched = false;
                for (var end = s + 1; end <= text.Count && !matched; end++)
                {
                    if (text[end - 1] is { } at && Ends.Contains(at, StringComparison.Ordinal))
                        break;
                    matched = !IsDotSegment(s, end) && From(t + 1, end);
                }
            }

            memo[(t, s)] = matched;
            return matched;
        }

        // Whether the text from start to end is exactly . or .., which a {name} never stands for.
        bool IsDotSegment(int start, int end) =>
            end - start is 1 or 2 && Enumerable.Range(start, end - start).All(i => text[i] == '.');
    }

    /// <summary>Where a URL's scheme and host end: at the first <c>/</c>, <c>?</c> or <c>#</c> after <c>://</c>, or its end.</summary>
    private static int HostEnd(string url)
    {
        var start = url.IndexOf("://", StringComparison.Ordinal);
        if (start < 0)
            return url.Length;
        var end = url.IndexOfAny(['/', '?', '#'], start + 3);
        return end < 0 ? url.Length : end;
    }
}
