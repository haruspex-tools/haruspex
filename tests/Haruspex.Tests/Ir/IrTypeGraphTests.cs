using Haruspex.Ir;

namespace Haruspex.Tests.Ir;

/// <summary>The document's named types as a graph: references, reachability, cycles, order and encodings.</summary>
public class IrTypeGraphTests
{
    private static readonly IrScalar Number = new(IrScalarKind.Number, IrFormat.Int32, "int");

    private static IrField Field(string name, IrType type) => new(name, type, IrPresence.Always, []);

    private static IrObjectType Type(params IrType[] fields) => new([.. fields.Select((t, i) => Field("f" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), t))]);

    private static IrDocument Document(Dictionary<string, IrObjectType> types, params (string Id, IrResult Result)[] results) => new(
        new IrSource("tsql"),
        [.. results.Select(r => new IrObject(r.Id, r.Id.Replace(".", "", StringComparison.Ordinal), IrObjectKind.Procedure, "p.sql:1:1", [], [r.Result]))],
        types,
        []);

    private static IrResult Json(IrType type) => new(IrEncoding.Json, type, []);

    private static IrResult Rows(IrType type) => new(IrEncoding.Rowset, type, []);

    [Fact]
    public void A_type_comes_after_the_types_it_refers_to()
    {
        var graph = IrTypeGraph.Of(Document(
            new() { ["Order"] = Type(new IrRef("Customer"), new IrArray(new IrRef("Line"))), ["Customer"] = Type(Number), ["Line"] = Type(new IrRef("Product")), ["Product"] = Type(Number) },
            ("s.p", Json(new IrRef("Order")))));

        Assert.Equal(["Customer", "Product", "Line", "Order"], graph.DependenciesFirst);
    }

    [Fact]
    public void The_order_follows_the_objects_by_id_and_then_lists_the_rest_by_name()
    {
        var graph = IrTypeGraph.Of(Document(
            new() { ["Z"] = Type(Number), ["Y"] = Type(Number), ["B"] = Type(Number), ["A"] = Type(Number) },
            ("s.second", Rows(new IrArray(new IrRef("Y")))),
            ("s.first", Rows(new IrArray(new IrRef("Z"))))));

        Assert.Equal(["Z", "Y", "A", "B"], graph.DependenciesFirst);
    }

    [Fact]
    public void A_cycle_is_found_with_every_type_on_it_and_none_beside_it()
    {
        // Node -> Part -> Node is a cycle; Leaf hangs off it; Self refers to itself alone.
        var graph = IrTypeGraph.Of(Document(
            new() { ["Node"] = Type(new IrRef("Part"), new IrRef("Leaf")), ["Part"] = Type(new IrArray(new IrRef("Node"))), ["Leaf"] = Type(Number), ["Self"] = Type(new IrRef("Self")) },
            ("s.p", Json(new IrRef("Node")))));

        Assert.True(graph.IsRecursive("Node"));
        Assert.True(graph.IsRecursive("Part"));
        Assert.True(graph.IsRecursive("Self"));
        Assert.False(graph.IsRecursive("Leaf"));
        Assert.True(graph.OnOneCycle("Node", "Part"));
        Assert.True(graph.OnOneCycle("Self", "Self"));
        Assert.False(graph.OnOneCycle("Node", "Leaf"));
        Assert.False(graph.OnOneCycle("Leaf", "Leaf"));
        Assert.False(graph.OnOneCycle("Node", "Self"));
    }

    [Fact]
    public void Within_a_cycle_the_type_entered_first_finishes_last()
    {
        var graph = IrTypeGraph.Of(Document(
            new() { ["Node"] = Type(new IrRef("Part")), ["Part"] = Type(new IrRef("Node")) },
            ("s.p", Json(new IrRef("Node")))));

        Assert.Equal(["Part", "Node"], graph.DependenciesFirst);
    }

    [Fact]
    public void References_are_followed_through_json_text_tuples_records_and_unions()
    {
        var graph = IrTypeGraph.Of(Document(
            new()
            {
                ["Root"] = Type(new IrJsonText(new IrRef("A")), new IrTuple([new IrElement(new IrRef("B"), [])]), new IrRecord(new IrRef("C"), []), new IrUnion([new IrRef("D"), Number])),
                ["A"] = Type(Number),
                ["B"] = Type(Number),
                ["C"] = Type(Number),
                ["D"] = Type(Number),
                ["Elsewhere"] = Type(Number),
            },
            ("s.p", Json(new IrRef("Root")))));

        Assert.Equal(["A", "B", "C", "D"], graph.ReferencesOf("Root"));
        Assert.Equal(new HashSet<string> { "Root", "A", "B", "C", "D" }, graph.ReachableFrom([new IrRef("Root")]));
    }

    [Fact]
    public void A_type_inside_json_text_arrives_as_json_even_in_a_rowset()
    {
        var graph = IrTypeGraph.Of(Document(
            new() { ["Row"] = Type(new IrJsonText(new IrRef("Payload"))), ["Payload"] = Type(Number), ["Unused"] = Type(Number) },
            ("s.p", Rows(new IrArray(new IrRef("Row"))))));

        Assert.Equal([IrEncoding.Rowset], graph.EncodingsOf("Row"));
        Assert.Equal([IrEncoding.Json], graph.EncodingsOf("Payload"));
        Assert.Empty(graph.EncodingsOf("Unused"));
    }

    [Fact]
    public void A_type_two_results_read_differently_has_both_encodings()
    {
        var graph = IrTypeGraph.Of(Document(
            new() { ["Shared"] = Type(Number) },
            ("s.rows", Rows(new IrArray(new IrRef("Shared")))),
            ("s.json", Json(new IrRef("Shared")))));

        Assert.Equal(new HashSet<IrEncoding> { IrEncoding.Rowset, IrEncoding.Json }, graph.EncodingsOf("Shared"));
    }

    [Fact]
    public void A_reference_to_no_type_is_refused_with_its_name()
    {
        var error = Assert.Throws<ArgumentException>(() => IrTypeGraph.Of(Document(new() { ["A"] = Type(new IrRef("Missing")) })));

        Assert.StartsWith("The type A refers to Missing, which names no type in the document.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_type_reached_two_ways_is_ordered_once_before_both()
    {
        var graph = IrTypeGraph.Of(Document(
            new() { ["Top"] = Type(new IrRef("Left"), new IrRef("Right")), ["Left"] = Type(new IrRef("Shared")), ["Right"] = Type(new IrRef("Shared")), ["Shared"] = Type(Number) },
            ("s.p", Json(new IrRef("Top")))));

        Assert.Equal(["Shared", "Left", "Right", "Top"], graph.DependenciesFirst);
        Assert.False(graph.IsRecursive("Shared"));
    }

    public static TheoryData<string> Containers => ["union", "record", "tuple", "json text"];

    [Theory]
    [MemberData(nameof(Containers))]
    public void A_cycle_through_any_container_is_found(string container)
    {
        IrType Around(IrType reference) => container switch
        {
            "union" => new IrUnion([reference, Number]),
            "record" => new IrRecord(reference, []),
            "tuple" => new IrTuple([new IrElement(reference, [])]),
            _ => new IrJsonText(reference),
        };
        var graph = IrTypeGraph.Of(Document(
            new() { ["A"] = Type(Around(new IrRef("B"))), ["B"] = Type(Around(new IrRef("A"))) },
            ("s.p", Json(new IrRef("A")))));

        Assert.True(graph.OnOneCycle("A", "B"));
    }

    [Fact]
    public void A_type_outside_a_cycle_that_refers_into_it_comes_after_the_whole_cycle()
    {
        var graph = IrTypeGraph.Of(Document(
            new() { ["Outside"] = Type(new IrRef("A")), ["A"] = Type(new IrRef("B")), ["B"] = Type(new IrRef("A")) },
            ("s.p", Json(new IrRef("Outside")))));

        Assert.Equal(["B", "A", "Outside"], graph.DependenciesFirst);
        Assert.False(graph.IsRecursive("Outside"));
    }

    /// <summary>
    /// The graph's laws over 500 generated documents (seeded, so a failure repeats): the order lists every type once; every
    /// reference that is not on a cycle points to a type earlier in the order; a type is recursive exactly when it reaches
    /// itself; two types share a cycle exactly when each reaches the other; and what is reachable is what a plain search finds.
    /// Reachability here is found apart from the graph, by following references field by field.
    /// </summary>
    [Fact]
    public void The_graph_keeps_its_laws_on_generated_documents()
    {
        for (var seed = 0; seed < 500; seed++)
        {
            var document = Generated(new Random(seed));
            var graph = IrTypeGraph.Of(document);
            var names = document.Types.Keys.ToList();
            var position = graph.DependenciesFirst.Select((n, i) => (n, i)).ToDictionary(x => x.n, x => x.i);
            HashSet<string> Reaches(string from) => Searched(document, document.Types[from].Fields.Select(f => f.Type));

            Assert.Equal(names.Order(StringComparer.Ordinal), graph.DependenciesFirst.Order(StringComparer.Ordinal));
            foreach (var a in names)
            {
                var reached = Reaches(a);
                Assert.True(graph.IsRecursive(a) == reached.Contains(a), $"seed {seed}: {a}");
                foreach (var b in graph.ReferencesOf(a).Where(b => !graph.OnOneCycle(a, b)))
                    Assert.True(position[b] < position[a], $"seed {seed}: {a} refers to {b}, which comes later");
                foreach (var b in names.Where(b => b != a))
                    Assert.True(graph.OnOneCycle(a, b) == (reached.Contains(b) && Reaches(b).Contains(a)), $"seed {seed}: {a}, {b}");
            }

            var roots = document.Objects.SelectMany(o => o.Results ?? []).Select(r => r.Type).ToList();
            Assert.True(graph.ReachableFrom(roots).SetEquals(Searched(document, roots)), $"seed {seed}");
        }
    }

    /// <summary>The named types a search reaches from some types, through every reference in every field.</summary>
    private static HashSet<string> Searched(IrDocument document, IEnumerable<IrType> roots)
    {
        var reached = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(roots.SelectMany(IrTypeTree.Within).OfType<IrRef>().Select(r => r.Name));
        while (pending.TryPop(out var name))
        {
            if (!reached.Add(name))
                continue;
            foreach (var next in document.Types[name].Fields.SelectMany(f => IrTypeTree.Within(f.Type)).OfType<IrRef>())
                pending.Push(next.Name);
        }

        return reached;
    }

#pragma warning disable CA5394 // A seeded Random gives the same documents on every run, which is the point; nothing here is secret.
    /// <summary>A document of one to eight types, each with up to three fields that refer to a random type in a random container.</summary>
    private static IrDocument Generated(Random random)
    {
        var names = Enumerable.Range(0, random.Next(1, 9)).Select(i => "T" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList();
        IrType Field()
        {
            var reference = new IrRef(names[random.Next(names.Count)]);
            return random.Next(7) switch
            {
                0 => Number,
                1 => new IrArray(reference),
                2 => new IrUnion([reference, Number]),
                3 => new IrRecord(reference, []),
                4 => new IrTuple([new IrElement(Number, []), new IrElement(reference, [])]),
                5 => new IrJsonText(reference),
                _ => reference,
            };
        }

        var types = names.ToDictionary(n => n, _ => Type([.. Enumerable.Range(0, random.Next(4)).Select(_ => Field())]));
        return Document(types, ("s.p", Json(new IrRef(names[random.Next(names.Count)]))));
    }
#pragma warning restore CA5394
}
