namespace Haruspex.Ir;

/// <summary>
/// What counts as an identifier, for every part of haruspex that writes names or paths: the names settled for
/// generated code, the keys an emitter writes without quotes, and the keys a path writes as <c>.key</c> rather than
/// <c>["key"]</c>.
/// </summary>
/// <remarks>
/// One definition, because two used to disagree: the emitter accepted <c>$</c> and the path renderer did not, so a key
/// <c>a$b</c> was <c>.a$b</c> in one place and <c>["a$b"]</c> in another. The rule is the format's own: an ASCII letter,
/// <c>_</c> or <c>$</c>, then letters, digits, <c>_</c> and <c>$</c>. It is the ASCII part of what most languages accept, so a
/// name means the same in every target and no Unicode category tables are needed; a non-ASCII key is still written,
/// quoted. Which identifiers a target language reserves for itself is the target's to say, and its emitter's to refuse (the
/// Zod emitter's <c>TypeScriptNames</c>); the IR knows no language's keywords.
/// </remarks>
public static class IrIdentifiers
{
    /// <summary>Whether <paramref name="name"/> is an identifier by the rule above.</summary>
    public static bool IsIdentifier(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Length > 0
            && (char.IsAsciiLetter(name[0]) || name[0] is '_' or '$')
            && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '$');
    }
}
