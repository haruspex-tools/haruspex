using System.Reflection;
using Haruspex.SqlServer.Model;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.Tests.Architecture;

/// <summary>
/// The model shows ScriptDom only as code: a body's statements, a query, an expression. Its names, types, parameters, options
/// and kinds are its own values, so a tool that reads the model needs ScriptDom only to read code (decision 0031,
/// <c>docs/decisions/0031-the-model-is-closed-hierarchies-of-records.md</c>).
/// </summary>
public class ModelSurfaceTests
{
    /// <summary>The ScriptDom types the model may show: the code of a body, and expressions.</summary>
    private static readonly Type[] Allowed = [typeof(StatementList), typeof(SelectStatement), typeof(ScalarExpression), typeof(BooleanExpression)];

    [Fact]
    public void The_model_shows_the_parsers_types_only_as_bodies_and_expressions()
    {
        var leaks = Leaks(typeof(DatabaseModel).Assembly.GetExportedTypes());

        Assert.True(leaks.Count == 0, "The model shows ScriptDom's types beyond bodies and expressions:\n" + string.Join("\n", leaks));
    }

    [Fact]
    public void The_check_finds_the_parsers_types_shown_through_any_member()
    {
        // So that the check can't pass by seeing nothing: each way a type can be shown is planted once.
        var leaks = Leaks([typeof(Planted)]);

        Assert.Equal(
            [
                "Planted..ctor(DataTypeReference): parameter",
                "Planted.Leak(Identifier): parameter",
                "Planted.Name: SchemaObjectName",
                "Planted.Names: IReadOnlyList`1[SchemaObjectName]",
                "Planted.Returned(): ProcedureParameter",
                "Planted.Type: DataTypeReference",
            ],
            leaks);
    }

    /// <summary>
    /// Every public or protected member of the types that shows a ScriptDom type outside <see cref="Allowed"/>: as a
    /// property's, field's or event's type, a parameter, a return type, a base type or an interface, at any depth of generic
    /// arguments and arrays.
    /// </summary>
    private static List<string> Leaks(IEnumerable<Type> types)
    {
        var leaks = new List<string>();
        const BindingFlags Shown = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var type in types)
        {
            if (type.BaseType is { } baseType && Leaked(baseType))
                leaks.Add($"{type.Name}: base {Describe(baseType)}");
            leaks.AddRange(type.GetInterfaces().Where(Leaked).Select(i => $"{type.Name}: interface {Describe(i)}"));

            foreach (var member in type.GetMembers(Shown).Where(IsShown))
            {
                switch (member)
                {
                    case PropertyInfo property when Leaked(property.PropertyType):
                        leaks.Add($"{type.Name}.{property.Name}: {Describe(property.PropertyType)}");
                        break;
                    case FieldInfo field when Leaked(field.FieldType):
                        leaks.Add($"{type.Name}.{field.Name}: {Describe(field.FieldType)}");
                        break;
                    case EventInfo shownEvent when shownEvent.EventHandlerType is { } handler && Leaked(handler):
                        leaks.Add($"{type.Name}.{shownEvent.Name}: {Describe(handler)}");
                        break;
                    case MethodBase method:
                        if (method is MethodInfo { ReturnType: var returned } && Leaked(returned) && !method.IsSpecialName)
                            leaks.Add($"{type.Name}.{method.Name}(): {Describe(returned)}");
                        if (method.GetParameters().Any(p => Leaked(p.ParameterType)))
                            leaks.Add($"{type.Name}.{method.Name}({string.Join(", ", method.GetParameters().Select(p => Describe(p.ParameterType)))}): parameter");
                        break;
                }
            }
        }

        return [.. leaks.Distinct().Order(StringComparer.Ordinal)];
    }

    /// <summary>Whether a member is part of the public surface: public or protected, and not compiler-generated plumbing.</summary>
    private static bool IsShown(MemberInfo member) => member switch
    {
        MethodBase method => (method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly) && !(method.IsSpecialName && method.Name.StartsWith("get_", StringComparison.Ordinal)),
        FieldInfo field => field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly,
        PropertyInfo property => property.GetMethod is { } get && (get.IsPublic || get.IsFamily || get.IsFamilyOrAssembly),
        EventInfo shownEvent => shownEvent.AddMethod is { IsPublic: true },
        _ => false,
    };

    /// <summary>Whether a type is, or is built from, a ScriptDom type the model may not show.</summary>
    private static bool Leaked(Type type)
    {
        if (type.HasElementType)
            return Leaked(type.GetElementType()!);
        if (type.IsGenericType && type.GetGenericArguments().Any(Leaked))
            return true;
        return type.Assembly == typeof(TSqlFragment).Assembly && !Allowed.Contains(type);
    }

    private static string Describe(Type type) =>
        type.IsGenericType ? $"{type.Name}[{string.Join(", ", type.GetGenericArguments().Select(Describe))}]" : type.Name;

    /// <summary>A type that shows ScriptDom every way a member can, for the check to find.</summary>
#pragma warning disable CA1812 // Read by reflection, never made.
    private sealed class Planted(DataTypeReference type)
    {
        public SchemaObjectName? Name { get; }

        public IReadOnlyList<SchemaObjectName> Names { get; } = [];

        public StatementList? Allowed { get; }

        public DataTypeReference Type { get; } = type;

        public static ProcedureParameter? Returned() => null;

        public static void Leak(Identifier identifier) => _ = identifier;
    }
#pragma warning restore CA1812
}
