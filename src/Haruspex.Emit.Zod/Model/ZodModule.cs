using Haruspex.Collections;
using Haruspex.Ir;

namespace Haruspex.Emit.Zod.Model;

/// <summary>A whole generated module, before it is written as text.</summary>
/// <param name="Header">Lines for the top of the file, each written as a line comment.</param>
/// <param name="Imports">The schemas imported from the application's modules, after Zod's own import.</param>
/// <param name="JsonTextHelper">Whether the module defines the helper that <see cref="ZodExpression.JsonText"/> calls.</param>
/// <param name="Declarations">The exported schemas, in the order written: each after the ones it refers to, but within a cycle.</param>
/// <param name="Folds">
/// The folds <see cref="ZodExpression.Canonical"/> sets name, by name, or null for none; when there are any, the module defines them and the helper
/// that maps a value by one.
/// </param>
internal sealed record ZodModule(
    EquatableArray<string> Header,
    EquatableArray<ZodImport> Imports,
    bool JsonTextHelper,
    EquatableArray<ZodDeclaration> Declarations,
    EquatableMap<string, IrFold>? Folds = null)
{
    /// <summary>The names the canonical value sets' helper and its folds bind in the module, which no imported name may take.</summary>
    public static IReadOnlyList<string> CanonicalNames { get; } = ["Fold", "canonicalForm", "canonicalOf", "folds"];

    /// <summary>The name the JSON text helper binds in the module, which no imported name may take.</summary>
    public const string JsonTextHelperName = "jsonText";

    /// <summary>The name Zod is imported under, which no imported name may take.</summary>
    public const string ZodName = "z";

    /// <summary>What follows a type's name in the name of its schema: <c>Order</c> and <c>OrderSchema</c>.</summary>
    public const string SchemaSuffix = "Schema";

    /// <summary>The name of the schema of the type <paramref name="name"/>.</summary>
    public static string SchemaOf(string name) => name + SchemaSuffix;

    /// <summary>
    /// What follows a procedure's name in the name of its parameters' type: <c>GetOrder</c> and <c>GetOrderParams</c>, whose
    /// schema is <c>GetOrderParamsSchema</c>.
    /// </summary>
    public const string ParametersSuffix = "Params";

    /// <summary>The name of the type of the parameters of the procedure named <paramref name="name"/>.</summary>
    public static string ParametersOf(string name) => name + ParametersSuffix;
}
