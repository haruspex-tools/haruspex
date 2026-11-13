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
/// Three lists say where equality is not generated field by field, so that each such place needs a line: the types compared
/// by identity because that is what they mean; the types with an equality written by hand, each with the test that holds its
/// laws; and the working types that are classes because nothing compares them. A list that names a type that no longer
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
    /// Types whose equality is written by hand, by full name, each with the test that holds its laws: a hand-written equality
    /// is a place a field added later can be forgotten, so it is trusted only where a test says what it must keep.
    /// </summary>
    private static readonly Dictionary<string, string> OwnEquality = new(StringComparer.Ordinal)
    {
        ["Haruspex.Collections.EquatableArray`1"] = "EquatableArrayTests.Equality_is_reflexive_symmetric_and_transitive_and_equal_sequences_hash_equal",
        ["Haruspex.Collections.EquatableMap`2"] = "EquatableMapTests.Equality_is_reflexive_symmetric_and_transitive_and_equal_maps_hash_equal",
        ["Haruspex.Ir.IrDocument"] = "IrJsonReaderTests.A_document_read_equals_one_whose_sorted_lists_were_in_another_order",
        ["Haruspex.SqlServer.Inference.CallContext"] = "ValueEqualityTests.The_equality_of_call_contexts_keeps_the_laws",
        ["Haruspex.SqlServer.Inference.FlowState"] = "ValueEqualityTests.The_equality_of_states_keeps_the_laws",
        ["Haruspex.SqlServer.Inference.ResultTrack"] = "ValueEqualityTests.The_equality_of_result_tracks_keeps_the_laws",
        ["Haruspex.SqlServer.Model.ObjectName"] = "ObjectNameTests.Its_equality_keeps_the_laws",
        ["Haruspex.SqlServer.Model.SqlType"] = "SqlTypeTests.Its_equality_keeps_the_laws",
        ["Haruspex.SqlServer.Semantics.Bindings+Key"] = "BinderTests.A_query_read_again_with_the_same_names_is_bound_once",
        ["Haruspex.SqlServer.Semantics.NameEnvironment"] = "NameEnvironmentTests.Its_equality_keeps_the_laws",
    };

    /// <summary>
    /// Working types that are classes, not records, because nothing compares them, by full name, and why. A type used as a
    /// key, or compared in a fixed point, a cache or a join, must compare by value and so can't be one of these.
    /// </summary>
    private static readonly Dictionary<string, string> NotCompared = new(StringComparer.Ordinal)
    {
    };

    /// <summary>Records still holding a collection that compares by reference, by full name, until each is swept.</summary>
    private static readonly HashSet<string> NotYetSwept = new(StringComparer.Ordinal)
    {
        "Haruspex.Cli.HaruspexCli+Request",
        "Haruspex.Cli.Selection",
        "Haruspex.Config.ConfigApplied",
        "Haruspex.Config.ConfigReadResult",
        "Haruspex.Config.HaruspexConfig",
        "Haruspex.Config.JsonArrayValue",
        "Haruspex.Config.JsonObjectValue",
        "Haruspex.Config.NamePatternEntry",
        "Haruspex.Config.NamingReplacement",
        "Haruspex.Config.NamingRules",
        "Haruspex.Config.ProcedureOverride",
        "Haruspex.Config.ResultsDeclaration",
        "Haruspex.Config.SchemaNaming",
        "Haruspex.Config.SchemaSettings",
        "Haruspex.Config.SelectionResult",
        "Haruspex.Emit.Zod.Planning.ZodPlan",
        "Haruspex.SqlServer.Inference.DeclaredResults",
        "Haruspex.SqlServer.Inference.Facts+Frame",
        "Haruspex.SqlServer.Inference.FunctionTyping",
        "Haruspex.SqlServer.Inference.ProcedureAnalysis",
        "Haruspex.SqlServer.Inference.ProcedureAnalyzer+ChainType",
        "Haruspex.SqlServer.Inference.ProcedureAnalyzer+Fixing",
        "Haruspex.SqlServer.Inference.ProcedureAnalyzer+JsonPath",
        "Haruspex.SqlServer.Inference.ProcedureAnalyzer+Row",
        "Haruspex.SqlServer.Inference.ProcedureAnalyzer+SetNode",
        "Haruspex.SqlServer.Inference.ReadScope",
        "Haruspex.SqlServer.Inference.Typing.ConversionRow",
        "Haruspex.SqlServer.Inference.Walked",
        "Haruspex.SqlServer.Model.DeclaredTable",
        "Haruspex.SqlServer.Model.Function",
        "Haruspex.SqlServer.Model.Procedure",
        "Haruspex.SqlServer.Model.Table",
        "Haruspex.SqlServer.Model.UniqueKey",
        "Haruspex.SqlServer.Model.View",
        "Haruspex.SqlServer.Semantics.AmbiguousCallee",
        "Haruspex.SqlServer.Semantics.Binder+Adding",
        "Haruspex.SqlServer.Semantics.BoundOutput",
        "Haruspex.SqlServer.Semantics.BoundSpecification",
        "Haruspex.SqlServer.Semantics.BoundWith",
        "Haruspex.SqlServer.Semantics.CheckReader+Proved",
        "Haruspex.SqlServer.Semantics.ConversionChart+Row",
        "Haruspex.SqlServer.Semantics.DmlTarget",
        "Haruspex.SqlServer.Semantics.RelationSymbol",
        "Haruspex.SqlServer.Semantics.SeveralObjects",
        "Haruspex.SqlServer.Semantics.ValueSetFact",
        "Haruspex.SqlServer.Sources.Enforcement",
        "Haruspex.SqlServer.Sources.SchemaReadResult",
    };

    [Fact]
    public void Every_record_of_every_layer_compares_each_field_by_value()
    {
        var failures = Layers().SelectMany(Types).Where(t => !NotYetSwept.Contains(t.FullName!)).SelectMany(t => Failures([t])).ToList();

        Assert.True(failures.Count == 0, $"{failures.Count} fields compare by reference, so their records do too:\n{string.Join("\n", failures)}");
    }

    [Fact]
    public void A_record_listed_as_not_yet_swept_still_needs_it()
    {
        var failing = Layers().SelectMany(Types).Where(t => Failures([t]).Any()).Select(t => t.FullName).ToHashSet(StringComparer.Ordinal);

        Assert.Empty(NotYetSwept.Where(name => !failing.Contains(name)).Order(StringComparer.Ordinal));
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
    public void The_check_finds_a_list_a_map_a_set_and_an_array_in_a_record()
    {
        // So that the check can't pass by seeing nothing: each kind of collection is planted once, and the two that do compare
        // by value are not reported.
        Assert.Equal(
            [
                $"{typeof(Planted).FullName}.Array: {typeof(int[])}",
                $"{typeof(Planted).FullName}.List: {typeof(IReadOnlyList<string>)}",
                $"{typeof(Planted).FullName}.Map: {typeof(ImmutableDictionary<string, int>)}",
                $"{typeof(Planted).FullName}.Set: {typeof(HashSet<string>)}",
            ],
            Failures([typeof(Planted)]).Order(StringComparer.Ordinal));
    }

    /// <summary>A record with a field of each kind that compares by reference, and two that compare by value.</summary>
    private sealed record Planted(IReadOnlyList<string> List, ImmutableDictionary<string, int> Map, HashSet<string> Set, int[] Array, EquatableArray<string> Fine, EquatableMap<string, int> AlsoFine)
    {
        public static Planted Of(string item) => new([item], [], [item], [1], [item], []);
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
        if (Nullable.GetUnderlyingType(type) is { } underlying)
            return ComparesByValue(underlying);
        if (type.IsGenericParameter || type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal))
            return true;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(EquatableArray<>))
            return ComparesByValue(type.GetGenericArguments()[0]);
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(EquatableMap<,>))
            return type.GetGenericArguments().All(ComparesByValue);
        if (ComparedByIdentity.Keys.Any(t => t.IsAssignableFrom(type)))
            return true;
        if (type.IsArray || type.Namespace?.StartsWith("System.Collections", StringComparison.Ordinal) == true)
            return false;

        // An interface of haruspex compares by value when every type of the layers that implements it does.
        if (type.IsInterface)
        {
            var implementations = Layers().SelectMany(Types).Where(t => !t.IsInterface && !t.IsAbstract && type.IsAssignableFrom(t)).ToList();
            return type.Assembly.GetName().Name!.StartsWith("Haruspex", StringComparison.Ordinal) && implementations.Count > 0 && implementations.All(ComparesByValue);
        }

        return IsRecord(type) || HasOwnEquality(type);
    }

    /// <summary>A field's name as written: a property's, for its backing field.</summary>
    private static string Name(FieldInfo field) =>
        field.Name.StartsWith('<') ? field.Name[1..field.Name.IndexOf('>', StringComparison.Ordinal)] : field.Name;
}
