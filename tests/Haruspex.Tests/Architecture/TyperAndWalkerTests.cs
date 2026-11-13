using System.Reflection;
using Haruspex.SqlServer.Inference;

namespace Haruspex.Tests.Architecture;

/// <summary>
/// The typer holds no state and reads none of the walker's: an expression's value is a function of the expression, the scope
/// it is read in (<see cref="ReadScope"/>) and what the typer was built with, the same for every read. So, read from the assembly, every field of the typer and of
/// its collaborator <see cref="Cardinality"/> is readonly and none holds a walker, and no method of theirs (a lambda's, a local
/// function's and a nested type's included) uses a member of the <see cref="Walker"/> or of a type nested in it: a field, a
/// property, a method or a constructor.
/// </summary>
public class TyperAndWalkerTests
{
    /// <summary>The types of the expression semantics, which hold no state and read none of the walker's.</summary>
    private static readonly Type[] TyperSide = [typeof(Typer), typeof(Cardinality)];

    [Fact]
    public void The_typer_holds_only_readonly_fields_and_no_walker()
    {
        var found = TyperSide.SelectMany(t => FieldsBreaking(t, typeof(Walker))).ToList();

        Assert.True(found.Count == 0, "The typer holds state:\n" + string.Join("\n", found));
    }

    [Fact]
    public void The_typer_uses_no_member_of_the_walker()
    {
        var found = UsesOf(typeof(Typer).Assembly, TyperSide, typeof(Walker));

        Assert.True(found.Count == 0, "The typer reads the walker:\n" + string.Join("\n", found));
    }

    [Fact]
    public void The_checks_find_a_mutable_field_a_walker_held_and_a_read_through_a_field_a_property_a_method_and_a_lambda()
    {
        // So that the checks can't pass by seeing nothing: each break is planted once, in types that stand for the two, which
        // run as written.
        var planted = new PlantedTyper();
        Assert.Equal(3, planted.ByMethod());
        Assert.Equal(4, planted.InLambda()(new PlantedWalker.Row(4)));

        Assert.Equal(
            [
                "PlantedTyper.count is not readonly.",
                "PlantedTyper.walker holds a PlantedWalker.",
            ],
            FieldsBreaking(typeof(PlantedTyper), typeof(PlantedWalker)));

        Assert.Equal(
            [
                "PlantedTyper..ctor: TyperAndWalkerTests+PlantedWalker..ctor",
                "PlantedTyper.ByField: TyperAndWalkerTests+PlantedWalker.Field",
                "PlantedTyper.ByMethod: TyperAndWalkerTests+PlantedWalker.Method",
                "PlantedTyper.ByProperty: TyperAndWalkerTests+PlantedWalker.get_Property",
                "PlantedTyper.InLambda: TyperAndWalkerTests+PlantedWalker+Row.get_Value",
            ],
            UsesOf(typeof(TyperAndWalkerTests).Assembly, [typeof(PlantedTyper)], typeof(PlantedWalker)));
    }

    /// <summary>
    /// The fields <paramref name="type"/> declares that are not readonly or hold a <paramref name="walker"/>, or a type nested
    /// in it, alone or in a generic type's arguments or an array. A constant is readonly. A nested type's fields are its own,
    /// made for one read, not the typer's.
    /// </summary>
    private static List<string> FieldsBreaking(Type type, Type walker)
    {
        var found = new List<string>();
        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            if (!field.IsInitOnly && !field.IsLiteral)
                found.Add($"{type.Name}.{field.Name} is not readonly.");
            if (Mentions(field.FieldType, walker))
                found.Add($"{type.Name}.{field.Name} holds a {walker.Name}.");
        }

        return [.. found.Order(StringComparer.Ordinal)];
    }

    /// <summary>Whether a type is <paramref name="walker"/>, nested in it, or built from one.</summary>
    private static bool Mentions(Type type, Type walker)
    {
        for (var outer = type; outer is not null; outer = outer.DeclaringType)
        {
            if (outer == walker)
                return true;
        }

        return (type.HasElementType && Mentions(type.GetElementType()!, walker))
            || (type.IsGenericType && type.GetGenericArguments().Any(a => Mentions(a, walker)));
    }

    /// <summary>
    /// Each member of <paramref name="walker"/>, or of a type nested in it, that a method of one of <paramref name="types"/>
    /// uses, as its IL says (<see cref="MemberReads.Own"/>).
    /// </summary>
    private static List<string> UsesOf(Assembly assembly, Type[] types, Type walker)
    {
        static string Nested(Type type) => type.FullName![(type.Namespace!.Length + 1)..];
        var names = types.Select(Nested).ToList();
        var owner = Nested(walker);
        return [.. MemberReads.Own(assembly.Location, t => names.Any(n => t == n || t.StartsWith(n + "+", StringComparison.Ordinal)))
            .Where(r => r[(r.IndexOf(": ", StringComparison.Ordinal) + 2)..] is var member
                && (member.StartsWith(owner + ".", StringComparison.Ordinal) || member.StartsWith(owner + "+", StringComparison.Ordinal)))];
    }

    /// <summary>Stands for the walker: state of its own, read through a field, a property, a method and a nested type.</summary>
    private sealed class PlantedWalker
    {
        public int Field = 1;

        public int Property { get; set; } = 2;

        public int Method() => Field + Property;

        /// <summary>Stands for a type the walker owns, such as its <c>Outcome</c>.</summary>
        public sealed record Row(int Value);
    }

    /// <summary>
    /// Stands for a typer that breaks each rule once; only <see cref="The_checks_find_a_mutable_field_a_walker_held_and_a_read_through_a_field_a_property_a_method_and_a_lambda"/>
    /// reads it, from its IL.
    /// </summary>
    private sealed class PlantedTyper
    {
        private readonly PlantedWalker walker = new();
        private int count;

        public int ByField() => walker.Field;

        public int ByProperty() => walker.Property;

        public int ByMethod() => walker.Method();

        public Func<PlantedWalker.Row, int> InLambda() => row => row.Value + count++;
    }
}
