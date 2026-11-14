using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Haruspex.Collections;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.Tests.Architecture;

/// <summary>
/// Every record compares by value (decision 0022): two records are equal when every field is, so each field's type must
/// compare by value too. A record's generated equality compares a field with the field type's own equality, which for
/// <see cref="IReadOnlyList{T}"/>, a dictionary and the rest of .NET's collections is the reference, so a record holding one
/// is equal only to itself, silently. That matters most where a fixed point compares states to see that it has converged.
/// This reads every record of every assembly under <c>src/</c> and fails on a field whose type does not compare by value.
/// </summary>
/// <remarks>
/// Four lists say where equality is not generated field by field, so that each such place needs a line: the types compared
/// by identity because that is what they mean; the types from outside haruspex that compare by value; the types with an
/// equality written by hand, each with the test that holds its laws; and the working types that are classes because nothing
/// compares them. A list that names a type that no longer
/// needs its line fails too, so the lists stay true.
/// </remarks>
public class RecordEqualityTests
{
    /// <summary>
    /// Types compared by identity because their identity is what they mean, and why. A record may hold one: two records
    /// holding the same one are about the same thing. None of them is a value, such as a name or a type, that two places
    /// could each build.
    /// </summary>
    private static readonly Dictionary<Type, string> ComparedByIdentity = new()
    {
        [typeof(TSqlFragment)] = "A syntax node is a place in a parsed body: two nodes of the same text are two places.",
        [typeof(BuiltInType)] = "An entry of the catalogue of built-in types, made once: the one object for its type.",
        [typeof(BuiltInFunction)] = "An entry of the catalogue of built-in functions, made once: the one object for its function.",
        [typeof(DatabaseModel)] = "The model a run reads, made once: the records that hold it are about that one model.",
        [typeof(QueryScope)] = "A scope binding made for one place in a body: two places with the same names are two scopes.",
        [typeof(Delegate)] = "A rule given as a function, written once where it is declared: equal when it is the same method on the same target.",
    };

    /// <summary>
    /// Types from outside haruspex that compare by value, each with what vouches for it: a type outside can't be read for its
    /// fields, so it is accepted only when listed here.
    /// </summary>
    private static readonly Dictionary<Type, string> OutsideByValue = new()
    {
        [typeof(System.Numerics.BigInteger)] = "An integer of any size, equal to another of the same value (.NET's documented equality).",
    };

    /// <summary>
    /// Types whose equality is written by hand, by full name, each with the test that holds its laws: a hand-written equality
    /// is a place a field added later can be forgotten, so it is trusted only where a test says what it must keep.
    /// </summary>
    private static readonly Dictionary<string, string> OwnEquality = new(StringComparer.Ordinal)
    {
        ["Haruspex.Collections.EquatableArray`1"] = "EquatableArrayTests.Equality_is_reflexive_symmetric_and_transitive_and_equal_sequences_hash_equal",
        ["Haruspex.Config.NamePattern"] = "NamePatternTests.Its_equality_keeps_the_laws",
        ["Haruspex.Config.NamingReplacement"] = "NamingRulesTests.A_rules_equality_keeps_the_laws",
        ["Haruspex.Config.ObjectPattern"] = "ObjectPatternTests.Its_equality_keeps_the_laws",
        ["Haruspex.Collections.EquatableMap`2"] = "EquatableMapTests.Equality_is_reflexive_symmetric_and_transitive_and_equal_maps_hash_equal",
        ["Haruspex.Ir.IrDocument"] = "IrJsonReaderTests.A_document_read_equals_one_whose_sorted_lists_were_in_another_order",
        ["Haruspex.SqlServer.Inference.ColumnSlot"] = "ValueEqualityTests.A_table_variables_column_is_one_place_whatever_the_case_of_its_names",
        ["Haruspex.SqlServer.Inference.ResultTrack"] = "ValueEqualityTests.The_equality_of_result_tracks_keeps_the_laws",
        ["Haruspex.SqlServer.Model.ObjectName"] = "ObjectNameTests.Its_equality_keeps_the_laws",
        ["Haruspex.SqlServer.Semantics.LocalName"] = "LocalNameTests.Its_equality_keeps_the_laws",
        ["Haruspex.SqlServer.Semantics.NameEnvironment"] = "NameEnvironmentTests.Its_equality_keeps_the_laws",
    };

    /// <summary>
    /// Working types that are classes, not records, because nothing compares them, by full name, and why. A type used as a
    /// key, or compared in a fixed point, a cache or a join, must compare by value and so can't be one of these.
    /// </summary>
    private static readonly Dictionary<string, string> NotCompared = new(StringComparer.Ordinal)
    {
        ["Haruspex.Cli.Generate+Plan"] = "A step's value, handed to the next once: nothing compares two runs' plans.",
        ["Haruspex.Cli.Generate+Loaded"] = "A step's value, holding the project read, which compares by identity: nothing compares two.",
        ["Haruspex.Cli.Generate+Selection"] = "A step's value, handed to the next once: nothing compares two selections.",
        ["Haruspex.Cli.Generate+Output"] = "A step's value, handed to the next once: nothing compares two outputs.",
        ["Haruspex.Emit.Zod.Planning.ZodPlan"] = "A stage's output, handed to the next once: nothing compares two plans.",
        ["Haruspex.SqlServer.Inference.Facts+Frame"] = "What one query or branch proved, its sources a set by identity: the environment is read, never compared.",
        ["Haruspex.SqlServer.Inference.Cardinality+Fixing"] = "A condition and the sources it may fix, a set by identity, within one query's reading.",
        ["Haruspex.SqlServer.Inference.ReadScope"] = "What a source holds, keyed by the source by identity: two sources of one table in two scopes are equal as records.",
        ["Haruspex.SqlServer.Semantics.Binder+Adding"] = "A builder of one query's sources: each copy adds to the one list, and nothing compares two.",
    };

    [Fact]
    public void Every_record_of_every_layer_compares_each_field_by_value()
    {
        var failures = Failures(Layers().SelectMany(Types)).ToList();

        Assert.True(failures.Count == 0, $"{failures.Count} fields compare by reference, so their records do too:\n{string.Join("\n", failures)}");
    }

    [Fact]
    public void Every_hand_written_equality_is_listed_with_the_test_that_holds_its_laws()
    {
        var own = Layers().SelectMany(Types).Where(t => !t.IsInterface && HasOwnEquality(t)).Select(t => t.FullName!).ToHashSet(StringComparer.Ordinal);
        var tests = typeof(RecordEqualityTests).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods())
            .Where(m => m.IsDefined(typeof(FactAttribute)) || m.IsDefined(typeof(TheoryAttribute)))
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(own.All(OwnEquality.ContainsKey), "Equality written by hand, with no test listed for its laws:\n" + string.Join("\n", own.Where(name => !OwnEquality.ContainsKey(name)).Order(StringComparer.Ordinal)));
        Assert.Empty(OwnEquality.Keys.Where(name => !own.Contains(name)).Order(StringComparer.Ordinal));
        Assert.Empty(OwnEquality.Where(e => !tests.Contains(e.Value)).Select(e => $"{e.Key}: {e.Value}").Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_type_listed_as_not_compared_is_a_class_that_compares_by_reference()
    {
        var types = Layers().SelectMany(Types).ToLookup(t => t.FullName, StringComparer.Ordinal);

        Assert.Empty(NotCompared.Keys.Where(name => types[name].SingleOrDefault() is not { } type || IsRecord(type) || type.IsValueType || HasOwnEquality(type)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_record_holding_a_list_is_equal_only_to_itself() =>
        // The trap the check is for: equal contents, built apart, and the records differ.
        Assert.NotEqual(Planted.Of("x"), Planted.Of("x"));

    [Fact]
    public void The_check_finds_a_list_a_map_a_set_an_array_a_tuple_of_a_list_an_object_and_an_outside_type_in_a_record()
    {
        // So that the check can't pass by seeing nothing: each kind of collection is planted once, and the two that do compare
        // by value are not reported.
        Assert.Equal(
            [
                $"{typeof(Planted).FullName}.Anything: {typeof(object)}",
                $"{typeof(Planted).FullName}.Array: {typeof(int[])}",
                $"{typeof(Planted).FullName}.List: {typeof(IReadOnlyList<string>)}",
                $"{typeof(Planted).FullName}.Map: {typeof(ImmutableDictionary<string, int>)}",
                $"{typeof(Planted).FullName}.Pair: {typeof((IReadOnlyList<int> Items, int Count))}",
                $"{typeof(Planted).FullName}.Set: {typeof(HashSet<string>)}",
                $"{typeof(Planted).FullName}.Uri: {typeof(Uri)}",
            ],
            Failures([typeof(Planted)]).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// A record with a field of each kind that compares by reference, or may: a list, a map, a set, an array, a tuple holding a
    /// list, an object, and a type from outside haruspex with an equality of its own that nothing here vouches for; and three
    /// that compare by value, a tuple and a nullable value among them.
    /// </summary>
    private sealed record Planted(
        IReadOnlyList<string> List,
        ImmutableDictionary<string, int> Map,
        HashSet<string> Set,
        int[] Array,
        (IReadOnlyList<int> Items, int Count) Pair,
        object Anything,
        Uri Uri,
        EquatableArray<string> Fine,
        (EquatableMap<string, int> Map, int? Count) AlsoFine)
    {
        public static Planted Of(string item) => new([item], [], [item], [1], ([1], 1), new object(), new("https://example.test/"), [item], ([], 1));
    }

    /// <summary>The assemblies of the layers, every project under <c>src/</c>.</summary>
    private static IEnumerable<Assembly> Layers() =>
        Directory.EnumerateFiles(Path.Combine(RepositoryPaths.Root, "src"), "*.csproj", SearchOption.AllDirectories)
            .Select(file => XDocument.Load(file).Descendants("AssemblyName").Select(e => e.Value.Trim()).FirstOrDefault() ?? Path.GetFileNameWithoutExtension(file))
            .Order(StringComparer.Ordinal)
            .Select(Assembly.Load);

    private static IEnumerable<Type> Types(Assembly assembly) => assembly.GetTypes().Where(t => !t.IsDefined(typeof(CompilerGeneratedAttribute)));

    /// <summary>Each field of a record among <paramref name="types"/> that compares by reference, as "Type.Field: FieldType".</summary>
    private static IEnumerable<string> Failures(IEnumerable<Type> types) =>
        from type in types
        where IsRecord(type) && !HasOwnEquality(type)
        from field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
        where !ComparesByValue(field.FieldType)
        select $"{type.FullName}.{Name(field)}: {field.FieldType}";

    /// <summary>A record class or record struct: the compiler gives it a <c>PrintMembers</c> of its own.</summary>
    private static bool IsRecord(Type type) =>
        type.GetMethod("PrintMembers", BindingFlags.Instance | BindingFlags.NonPublic, [typeof(System.Text.StringBuilder)]) is { } print
        && print.DeclaringType == type;

    /// <summary>
    /// Whether the type's equality is written by hand rather than generated: a record whose <c>Equals</c> is its own, or a class
    /// or struct that overrides <c>Equals</c>.
    /// </summary>
    private static bool HasOwnEquality(Type type)
    {
        const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly;
        var typed = type.GetMethod("Equals", Declared, [type]);
        var untyped = type.GetMethod("Equals", Declared, [typeof(object)]);
        return IsRecord(type)
            ? typed is not null && !typed.IsDefined(typeof(CompilerGeneratedAttribute))
            : typed is not null || untyped is not null;
    }

    private static bool ComparesByValue(Type type)
    {
        if (type.IsGenericParameter || type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal))
            return true;

        // A nullable value, a tuple and the two collections compare by what they hold, so what they hold is checked.
        if (type.IsGenericType && ComparedByArguments.Contains(type.GetGenericTypeDefinition()))
            return type.GetGenericArguments().All(ComparesByValue);
        if (ComparedByIdentity.Keys.Any(t => t.IsAssignableFrom(type)) || OutsideByValue.ContainsKey(type))
            return true;
        if (!type.Assembly.GetName().Name!.StartsWith("Haruspex", StringComparison.Ordinal))
            return false;

        // An interface of haruspex compares by value when every type of the layers that implements it does.
        if (type.IsInterface)
        {
            var implementations = Layers().SelectMany(Types).Where(t => !t.IsInterface && !t.IsAbstract && type.IsAssignableFrom(t)).ToList();
            return implementations.Count > 0 && implementations.All(ComparesByValue);
        }

        return IsRecord(type) || HasOwnEquality(type);
    }

    /// <summary>
    /// The generic types that compare by their type arguments' equality, item by item: a nullable value, the tuples, and the
    /// two collections of <c>Haruspex.Collections</c>. Any other type from outside haruspex, <see cref="object"/> among them,
    /// says nothing that can be checked, so a field of it is reported unless its type is listed as compared by identity.
    /// </summary>
    private static readonly HashSet<Type> ComparedByArguments =
    [
        typeof(Nullable<>), typeof(EquatableArray<>), typeof(EquatableMap<,>),
        typeof(ValueTuple<>), typeof(ValueTuple<,>), typeof(ValueTuple<,,>), typeof(ValueTuple<,,,>), typeof(ValueTuple<,,,,>), typeof(ValueTuple<,,,,,>), typeof(ValueTuple<,,,,,,>),
        typeof(Tuple<>), typeof(Tuple<,>), typeof(Tuple<,,>), typeof(Tuple<,,,>), typeof(Tuple<,,,,>), typeof(Tuple<,,,,,>), typeof(Tuple<,,,,,,>),
    ];

    /// <summary>A field's name as written: a property's, for its backing field.</summary>
    private static string Name(FieldInfo field) =>
        field.Name.StartsWith('<') ? field.Name[1..field.Name.IndexOf('>', StringComparison.Ordinal)] : field.Name;
}
