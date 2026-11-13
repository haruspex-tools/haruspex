using Haruspex.Collections;

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

    /// <summary>The name Zod is imported under, which no imported name may take.</summary>
    public const string ZodName = "z";

    /// <summary>What follows a type's name in the name of its schema: <c>Order</c> and <c>OrderSchema</c>.</summary>
    public const string SchemaSuffix = "Schema";

    /// <summary>The name of the schema of the type <paramref name="name"/>.</summary>
    public static string SchemaOf(string name) => name + SchemaSuffix;
}
