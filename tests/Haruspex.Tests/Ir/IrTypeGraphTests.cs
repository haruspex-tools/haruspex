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
}
