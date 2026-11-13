using System.Reflection;
using Haruspex.SqlServer.Inference;
using Haruspex.SqlServer.Model;

namespace Haruspex.Tests.Architecture;

/// <summary>
/// The typer holds no state and reads none of the walker's: an expression's value is a function of the expression, the scope
/// it is read in (<see cref="ReadScope"/>) and what the typer was built with, the same for every read
/// (<c>docs/decisions/0035-the-typer-apart-from-the-walker.md</c>). So, read from the assembly, every field of the typer and of
/// its collaborator <see cref="Cardinality"/> is readonly, none holds a walker, none holds a type whose instances can change
/// unless it is listed with why that is no state of a read (<see cref="ChangingButNoState"/>), and no method of theirs (a lambda's, a local
/// function's and a nested type's included) uses a member of the <see cref="Walker"/> or of a type nested in it: a field, a
/// property, a method or a constructor.
/// </summary>
public class TyperAndWalkerTests
{
    /// <summary>The types of the expression semantics, which hold no state and read none of the walker's.</summary>
    private static readonly Type[] TyperSide = [typeof(Typer), typeof(Cardinality)];

    /// <summary>
    /// The types the typer may hold although their instances change, each with why the change is no state of a read: what a
    /// read gives never depends on what was asked of them before.
    /// </summary>
    private static readonly Dictionary<Type, string> ChangingButNoState = new()
    {
        [typeof(SqlServer.Semantics.Binder)] = "Binds through Bindings, a memo whose answer to each question is a function of the question (decision 0033).",
        [typeof(Summaries)] = "Memo tables of summaries, each answer a function of the object and the way it is read; recursion is settled inside a question, never across two.",
        [typeof(DatabaseModel)] = "The schema, built before inference and only read during it.",
        [typeof(FunctionTyping)] = "A row of the static function table, whose rule is a delegate; each is made once, by a factory that returns it, from a lambda that captures only values that can't change (The_function_tables_rules_capture_only_values_that_cannot_change), and is handed the typer and the scope.",
    };

    /// <summary>Types from outside haruspex whose instances never change, which their fields alone don't show.</summary>
    private static readonly Type[] OutsideUnchanging = [typeof(string), typeof(decimal), typeof(System.Numerics.BigInteger)];

    [Fact]
    public void The_typer_holds_only_readonly_fields_of_types_that_do_not_change_and_no_walker()
    {
        var found = TyperSide.SelectMany(t => FieldsBreaking(t, typeof(Walker))).ToList();

        Assert.True(found.Count == 0, "The typer holds state:\n" + string.Join("\n", found));
    }

    [Fact]
    public void Each_type_listed_as_changing_does_change()
    {
        // So that a listing goes when its type stops changing.
        var stale = ChangingButNoState.Keys.Where(t => Changing(t, [], listed: false) is null).Select(t => t.Name).ToList();

        Assert.True(stale.Count == 0, "Listed as changing, but its instances can't:\n" + string.Join("\n", stale));
    }

    [Fact]
    public void The_function_tables_rules_capture_only_values_that_cannot_change()
    {
        // A delegate's target holds what its lambda captured, in fields the compiler doesn't mark readonly; the factory that
        // makes each rule assigns none after the lambda, so each holds what it was made with, which must not change itself.
        var capturing = Typer.FunctionTable
            .Where(t => t.Type?.Target is { } target && target.GetType()
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(f => Changing(f.FieldType, [], listed: true) is not null))
            .Select(t => t.Function.Name)
            .ToList();

        Assert.True(capturing.Count == 0, "A rule of the function table captures what can change:\n" + string.Join("\n", capturing));
    }

    [Fact]
    public void The_typer_uses_no_member_of_the_walker()
    {
        var found = UsesOf(typeof(Typer).Assembly, TyperSide, typeof(Walker));

        Assert.True(found.Count == 0, "The typer reads the walker:\n" + string.Join("\n", found));
    }

    [Fact]
    public void The_checks_find_a_mutable_field_a_changing_one_a_walker_held_and_a_read_through_a_field_a_property_a_method_and_a_lambda()
    {
        // So that the checks can't pass by seeing nothing: each break is planted once, in types that stand for the two, which
        // run as written.
        var planted = new PlantedTyper();
        Assert.Equal(3, planted.ByMethod());
        Assert.Equal(4, planted.InLambda()(new PlantedWalker.Row(4)));
        Assert.Equal(1, planted.Seen(1));

        Assert.Equal(
            [
                "PlantedTyper.count is not readonly.",
                "PlantedTyper.seen holds a List`1, whose List`1 can change.",
                "PlantedTyper.walker holds a PlantedWalker, whose Int32 Field is not readonly.",
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
    /// The fields <paramref name="type"/> declares that are not readonly, hold a type whose instances can change
    /// (<see cref="Changing"/>), or hold a <paramref name="walker"/>, or a type nested in it, alone or in a generic type's
    /// arguments or an array. A constant is readonly. A nested type's fields are its own, made for one read, not the typer's.
    /// </summary>
    private static List<string> FieldsBreaking(Type type, Type walker)
    {
        var found = new List<string>();
        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            if (!field.IsInitOnly && !field.IsLiteral)
                found.Add($"{type.Name}.{field.Name} is not readonly.");
            if (Changing(field.FieldType, [], listed: true) is { } why)
                found.Add($"{type.Name}.{field.Name} holds a {field.FieldType.Name}, whose {why}.");
            if (Mentions(field.FieldType, walker))
                found.Add($"{type.Name}.{field.Name} holds a {walker.Name}.");
        }

        return [.. found.Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// Why an instance of <paramref name="type"/> can change, or null when it can't: a type from outside haruspex can, unless it
    /// is a primitive, an enum, one of <see cref="OutsideUnchanging"/>, a nullable or immutable collection of unchanging types;
    /// an array can; a type of haruspex can when any field of it, or of a type deriving from it, is not readonly or holds one
    /// that can. With <paramref name="listed"/>, a type in <see cref="ChangingButNoState"/> counts as unchanging.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <param name="seen">The types being checked, which count as unchanging while they are, so that a cycle ends.</param>
    /// <param name="listed">Whether the types listed as changing but no state are let through.</param>
    private static string? Changing(Type type, HashSet<Type> seen, bool listed)
    {
        if (type.IsPrimitive || type.IsEnum || OutsideUnchanging.Contains(type) || !seen.Add(type) || (listed && ChangingButNoState.ContainsKey(type)))
            return null;
        if (type.IsArray)
            return $"{type.Name} can change";
        if (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(Nullable<>)
            || type.Namespace is "System.Collections.Immutable" or "System.Collections.Frozen"
            || type.FullName!.StartsWith("System.ValueTuple`", StringComparison.Ordinal)
            || type.GetGenericTypeDefinition() == typeof(Haruspex.Collections.EquatableArray<>)
            || type.GetGenericTypeDefinition() == typeof(Haruspex.Collections.EquatableMap<,>)))
        {
            return type.GetGenericArguments().Select(a => Changing(a, seen, listed)).FirstOrDefault(w => w is not null);
        }

        if (type.Namespace?.StartsWith("Haruspex", StringComparison.Ordinal) != true)
            return $"{type.Name} can change";

        var kinds = type.IsAbstract || type.IsInterface
            ? type.Assembly.GetTypes().Where(t => !t.IsAbstract && !t.IsInterface && type.IsAssignableFrom(t))
            : [type];
        foreach (var kind in kinds)
        {
            for (var level = kind; level is not null && level != typeof(object); level = level.BaseType)
            {
                foreach (var field in level.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (!field.IsInitOnly)
                        return $"{field.FieldType.Name} {field.Name} is not readonly";
                    if (Changing(field.FieldType, seen, listed) is { } why)
                        return why;
                }
            }
        }

        return null;
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
    /// Stands for a typer that breaks each rule once; only <see cref="The_checks_find_a_mutable_field_a_changing_one_a_walker_held_and_a_read_through_a_field_a_property_a_method_and_a_lambda"/>
    /// reads it, from its IL.
    /// </summary>
    private sealed class PlantedTyper
    {
        private readonly PlantedWalker walker = new();
        private readonly List<int> seen = [];
        private int count;

        public int ByField() => walker.Field;

        public int ByProperty() => walker.Property;

        public int ByMethod() => walker.Method();

        public Func<PlantedWalker.Row, int> InLambda() => row => row.Value + count++;

        public int Seen(int value)
        {
            seen.Add(value);
            return seen[^1];
        }
    }
}
