namespace Haruspex.Emit.Zod;

/// <summary>
/// The names TypeScript keeps for itself, which a generated binding (a schema's or a type's name) must not take. The IR's
/// names are identifiers by the format's own rule (<c>IrIdentifiers</c>); whether one is reserved depends on the language a
/// target writes, so it is the emitter's to know.
/// </summary>
public static class TypeScriptNames
{
    /// <summary>The language these words belong to, as messages name it.</summary>
    public const string Language = "TypeScript";

    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        // ECMAScript reserved words, strict mode included (modules run in strict mode).
        "await", "break", "case", "catch", "class", "const", "continue", "debugger", "default", "delete", "do", "else",
        "enum", "export", "extends", "false", "finally", "for", "function", "if", "implements", "import", "in",
        "instanceof", "interface", "let", "new", "null", "package", "private", "protected", "public", "return", "static",
        "super", "switch", "this", "throw", "true", "try", "typeof", "var", "void", "while", "with", "yield",
        "arguments", "eval",

        // TypeScript's built-in type names, which a type alias may not take (type string = …).
        "any", "bigint", "boolean", "never", "number", "object", "string", "symbol", "undefined", "unknown",
    };

    /// <summary>
    /// Whether <paramref name="name"/> cannot name a binding in a generated module: a reserved word of ECMAScript, or a name
    /// TypeScript keeps for a built-in type. Only lower-case names are reserved, so a Pascal-case name never is; camel case and
    /// preserved names can be. A reserved word is still a valid property name (<c>{ class: 1 }</c>), so keys may be one.
    /// </summary>
    public static bool IsReserved(string name) => Reserved.Contains(name);
}
