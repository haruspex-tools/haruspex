namespace Haruspex.Ir;

/// <summary>
/// What counts as an identifier, for every part of haruspex that writes names or paths: the names settled for
/// generated code, the keys an emitter writes without quotes, and the keys a path writes as <c>.key</c> rather than
/// <c>["key"]</c>.
/// </summary>
/// <remarks>
/// One definition, because two used to disagree: the emitter accepted <c>$</c> and the path renderer did not, so a key
/// <c>a$b</c> was <c>.a$b</c> in one place and <c>["a$b"]</c> in another. The rule is ECMAScript's IdentifierName
/// restricted to ASCII (a letter, <c>_</c> or <c>$</c>, then letters, digits, <c>_</c> and <c>$</c>). ASCII only, so a
/// name means the same in every target and no Unicode category tables are needed; a non-ASCII key is still written,
/// quoted. Reserved words are identifiers here, because they are valid property names (<c>{ class: 1 }</c>); a name
/// that becomes a binding, such as a type's name, must also not be one (<see cref="IsReserved"/>).
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

    /// <summary>
    /// Whether <paramref name="name"/> cannot name a binding: a reserved word of ECMAScript (strict mode, where modules
    /// run), or a name TypeScript keeps for a built-in type, which a type alias may not take (<c>type string = …</c>).
    /// Only lower-case names are reserved, so a Pascal-case name never is; camel case and preserved names can be.
    /// </summary>
    public static bool IsReserved(string name) => Reserved.Contains(name);

    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        // ECMAScript reserved words, strict mode included.
        "await", "break", "case", "catch", "class", "const", "continue", "debugger", "default", "delete", "do", "else",
        "enum", "export", "extends", "false", "finally", "for", "function", "if", "implements", "import", "in",
        "instanceof", "interface", "let", "new", "null", "package", "private", "protected", "public", "return", "static",
        "super", "switch", "this", "throw", "true", "try", "typeof", "var", "void", "while", "with", "yield",
        "arguments", "eval",

        // TypeScript's built-in type names, which a type alias may not take.
        "any", "bigint", "boolean", "never", "number", "object", "string", "symbol", "undefined", "unknown",
    };
}
