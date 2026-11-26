using Haruspex.Collections;
using Haruspex.SqlServer.Model.XmlSchemas;

namespace Haruspex.SqlServer.Semantics.XmlSchemas;

/// <summary>
/// The XQuery path of an XML method, in the small form whose nodes a schema can follow: named child elements, perhaps ending in an
/// attribute, each step with or without <c>[1]</c>, from the instance's top level (<c>/order/line/@sku</c>) or from the node a
/// method is called on (<c>line/@sku</c>, or <c>.</c>), perhaps in parentheses followed by <c>[1]</c>
/// (<c>(/order/@id)[1]</c>), after a prolog of <c>declare namespace</c> and <c>declare default element namespace</c>. A prefix the
/// prolog doesn't declare isn't read: none that XQuery declares itself names a node of a collection's schemas. Anything else
/// (a wildcard, <c>//</c>, an axis, another predicate, a function, a comment) is not read, and proves nothing.
/// Why: docs/decisions/0139-xml-is-typed-from-its-schema-collection-where-the-schema-proves-it.md.
/// </summary>
/// <param name="Absolute">Whether it starts at the instance's top level.</param>
/// <param name="Steps">Its steps, none for <c>.</c>.</param>
/// <param name="First">Whether the whole path is in parentheses followed by <c>[1]</c>, which keeps only its first node.</param>
public sealed record XmlPath(bool Absolute, EquatableArray<XmlPathStep> Steps, bool First)
{
    /// <summary>The path <paramref name="text"/> is, or null when it is not of the form read here.</summary>
    public static XmlPath? Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new Reader(text).Path();
    }

    /// <summary>A recursive-descent reader of one path, which gives up (null) at the first thing it doesn't read.</summary>
    private sealed class Reader(string text)
    {
        private readonly Dictionary<string, string> prefixes = new(StringComparer.Ordinal);
        private string defaultElements = "";
        private int at;

        public XmlPath? Path()
        {
            while (Keyword("declare"))
            {
                if (Keyword("namespace"))
                {
                    if (Name() is not { } prefix || !Symbol('=') || Literal() is not { } uri || !Symbol(';'))
                        return null;
                    prefixes[prefix] = uri;
                }
                else if (Keyword("default") && Keyword("element") && Keyword("namespace") && Literal() is { } uri && Symbol(';'))
                {
                    defaultElements = uri;
                }
                else
                {
                    return null;
                }
            }

            XmlPath? path;
            if (Symbol('('))
            {
                path = Steps();
                if (path is null || !Symbol(')') || !FirstOnly())
                    return null;
                path = path with { First = true };
            }
            else
            {
                path = Steps();
            }

            Space();
            return at == text.Length ? path : null;
        }

        private XmlPath? Steps()
        {
            Space();
            if (Symbol('.'))
                return new XmlPath(false, [], false);

            var absolute = Symbol('/');
            var steps = new List<XmlPathStep>();
            do
            {
                if (Peek('/'))
                    return null; // "//", any descendant.
                if (Step() is not { } step)
                    return null;
                steps.Add(step);
                if (step.IsAttribute)
                    break;
            }
            while (Symbol('/'));
            return new XmlPath(absolute, [.. steps], false);
        }

        private XmlPathStep? Step()
        {
            var attribute = Symbol('@');
            if (QualifiedName(attribute) is not { } name)
                return null;
            var first = Peek('[') && FirstOnly();
            return Peek('[') ? null : new XmlPathStep(name, attribute, first);
        }

        /// <summary>A name, its prefix bound: an element's unprefixed name is in the default element namespace, an attribute's in none.</summary>
        private XmlName? QualifiedName(bool attribute)
        {
            if (Name() is not { } first)
                return null;
            if (at < text.Length && text[at] == ':')
            {
                at++;
                return Name() is { } local && prefixes.TryGetValue(first, out var uri) ? new XmlName(uri, local) : null;
            }

            return new XmlName(attribute ? "" : defaultElements, first);
        }

        private bool FirstOnly()
        {
            var start = at;
            if (Symbol('[') && Symbol('1') && Symbol(']'))
                return true;
            at = start;
            return false;
        }

        private bool Keyword(string word)
        {
            Space();
            var start = at;
            if (Name() == word && (at == text.Length || !IsNameChar(text[at])))
                return true;
            at = start;
            return false;
        }

        /// <summary>An NCName: a letter or <c>_</c>, then letters, digits, <c>_</c>, <c>-</c> and <c>.</c>.</summary>
        private string? Name()
        {
            Space();
            var start = at;
            if (at < text.Length && (char.IsLetter(text[at]) || text[at] == '_'))
            {
                at++;
                while (at < text.Length && IsNameChar(text[at]))
                    at++;
            }

            return at > start ? text[start..at] : null;
        }

        private static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '-' or '.';

        /// <summary>A string literal in double or single quotes, without the escapes XQuery allows in one.</summary>
        private string? Literal()
        {
            Space();
            if (at >= text.Length || text[at] is not ('"' or '\''))
                return null;
            var quote = text[at];
            var end = text.IndexOf(quote, at + 1);
            if (end < 0 || (end + 1 < text.Length && text[end + 1] == quote) || text.AsSpan(at + 1, end - at - 1).Contains('&'))
                return null;
            var value = text[(at + 1)..end];
            at = end + 1;
            return value;
        }

        private bool Symbol(char symbol)
        {
            Space();
            if (at < text.Length && text[at] == symbol)
            {
                at++;
                return true;
            }

            return false;
        }

        private bool Peek(char symbol)
        {
            Space();
            return at < text.Length && text[at] == symbol;
        }

        private void Space()
        {
            while (at < text.Length && char.IsWhiteSpace(text[at]))
                at++;
        }
    }
}
