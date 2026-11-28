using System.Diagnostics.CodeAnalysis;
using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;

namespace Haruspex.Config;

/// <summary>
/// A procedure's or function's parameter declared to hold JSON of a schema (<c>parameters["shop.place_order.@order"].json</c>, or the
/// extended property <c>haruspex.jsonSchema</c> on the parameter), translated into the IR: what a caller's text parses to, a claim
/// about every caller, which the body's reads of the parameter take as given and the generated code checks before it sends
/// (docs/decisions/0143-a-parameter-s-json-may-be-declared.md).
/// </summary>
/// <param name="Owner">The procedure or function, as the key names it.</param>
/// <param name="Parameter">The parameter's name, with its <c>@</c>, as the key writes it.</param>
/// <param name="Content">What a non-null value of the parameter parses to.</param>
/// <param name="Types">The object types <paramref name="Content"/> refers to.</param>
/// <param name="Record">The declaration as the IR records it: the parameter, the schema file and pointer, and every file's hash.</param>
/// <param name="At">Where the declaration is written: in the config file, or in the SQL (<see cref="JsonSchemaAnnotations"/>).</param>
public sealed record ParameterDeclaration(ObjectName Owner, string Parameter, IrType Content, EquatableMap<string, IrObjectType> Types, IrParameterDeclaration Record, DiagnosticLocation At)
{
    /// <summary>
    /// The parameter a key of <c>parameters</c> names, as <c>schema.object.@name</c>, each part in brackets when it holds a dot or a
    /// bracket, as for <c>columns</c> (<see cref="ColumnDeclaration.TryParseKey"/>); or why the key is not one.
    /// </summary>
    public static bool TryParseKey(string key, out ObjectName owner, [NotNullWhen(true)] out string? parameter, [NotNullWhen(false)] out string? problem)
    {
        ArgumentNullException.ThrowIfNull(key);
        parameter = null;
        if (!ColumnDeclaration.TryParseKey(key, out owner, out var last, out var why))
        {
            problem = why.Replace("a column named schema.table.column", "a parameter named schema.object.@name", StringComparison.Ordinal);
            return false;
        }

        if (!last.StartsWith('@'))
        {
            problem = $"'{key}' names its parameter without its @: write schema.object.@name, as the procedure declares it.";
            return false;
        }

        (parameter, problem) = (last, null);
        return true;
    }

    /// <summary>The key the IR records a parameter by, <c>schema.object.@name</c>, each part as given, in brackets when it holds a dot or a bracket.</summary>
    public static string Key(ObjectName owner, string parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        return $"{Part(owner.Schema)}.{Part(owner.Name)}.{Part(parameter)}";
    }

    private static string Part(string name) =>
        name.AsSpan().IndexOfAny('.', '[', ']') < 0 ? name : $"[{name.Replace("]", "]]", StringComparison.Ordinal)}]";
}
