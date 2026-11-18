using Haruspex.Diagnostics;

namespace Haruspex.Emit.Zod;

/// <summary>
/// The codes of what the Zod emitter refuses (HAR5xxx), each explained on its page in <c>docs/diagnostics</c>: a document it
/// can't write as TypeScript, refused before anything is built.
/// </summary>
public static class ZodCodes
{
    /// <summary>A name in the IR that isn't a TypeScript identifier, or is a reserved word.</summary>
    public static DiagnosticCode BadName { get; } = new(5001, DiagnosticSeverity.Error, "An IR name isn't a TypeScript identifier");

    /// <summary>An imported name that isn't a TypeScript identifier, or is a reserved word.</summary>
    public static DiagnosticCode BadImportName { get; } = new(5002, DiagnosticSeverity.Error, "An imported name isn't a TypeScript identifier");

    /// <summary>One name imported from two modules, which one module can't bind twice.</summary>
    public static DiagnosticCode ImportedTwice { get; } = new(5003, DiagnosticSeverity.Error, "A name is imported from two modules");

    /// <summary>An imported name that the module also declares or uses.</summary>
    public static DiagnosticCode ImportClashes { get; } = new(5004, DiagnosticSeverity.Error, "An imported name the module also declares");

    /// <summary>A named type that one result reads as rows and another as JSON, whose scalars arrive differently in each.</summary>
    public static DiagnosticCode TwoEncodings { get; } = new(5005, DiagnosticSeverity.Error, "A type read both as rows and as JSON");

    /// <summary>A procedure whose parameters' schema would take a name the module already declares.</summary>
    public static DiagnosticCode ParametersNameTaken { get; } = new(5006, DiagnosticSeverity.Error, "A procedure's parameters' name is taken");

    /// <summary>A result set named with a key a TypeScript object can't hold (<see cref="TypeScriptNames.IsUnusableKey"/>).</summary>
    public static DiagnosticCode SetNameUnusable { get; } = new(5007, DiagnosticSeverity.Error, "A result set's name can't be a key");
}
