using Haruspex.Ir;

namespace Haruspex.Emit.Zod.Model;

/// <summary>A whole generated module, before it is written as text.</summary>
/// <param name="Header">Lines for the top of the file, each written as a line comment.</param>
/// <param name="Imports">The schemas imported from the application's modules, after Zod's own import.</param>
/// <param name="JsonTextHelper">Whether the module defines the helper that <see cref="ZodExpression.JsonText"/> calls.</param>
/// <param name="Declarations">The exported schemas, in the order written: each after the ones it refers to, but within a cycle.</param>
internal sealed record ZodModule(
    EquatableArray<string> Header,
    EquatableArray<ZodImport> Imports,
    bool JsonTextHelper,
    EquatableArray<ZodDeclaration> Declarations)
{
    /// <summary>The name the JSON text helper binds in the module, which no imported name may take.</summary>
    public const string JsonTextHelperName = "jsonText";
}
