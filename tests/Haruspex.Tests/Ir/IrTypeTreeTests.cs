using Haruspex.Ir;

namespace Haruspex.Tests.Ir;

/// <summary>The structure of one type: its children with their path segments, the nodes within it, and renaming references.</summary>
public class IrTypeTreeTests
{
    private static readonly IrScalar Text = new(IrScalarKind.String, null, "nvarchar(10)");
    private static readonly IrNullReason Because = new(IrNullRule.NullableColumn, "s.t.c");

    public static TheoryData<IrType> Leaves => [Text, new IrRef("A"), new IrImport("S", "./s"), new IrUnknownType(IrUnknownReason.UnsupportedExpression), new IrJsonText()];

    [Theory]
    [MemberData(nameof(Leaves))]
    public void A_leaf_has_no_children(IrType leaf) => Assert.Empty(IrTypeTree.Children(leaf));

    [Fact]
    public void Each_container_names_the_path_segment_to_its_children()
    {
        Assert.Equal([new IrTypeChild(Text, new IrPathItems())], IrTypeTree.Children(new IrArray(Text)));
        Assert.Equal([new IrTypeChild(Text, new IrPathValues())], IrTypeTree.Children(new IrRecord(Text, [])));
        Assert.Equal([new IrTypeChild(Text, new IrPathJson())], IrTypeTree.Children(new IrJsonText(Text)));
        Assert.Equal(
            [new IrTypeChild(Text, new IrPathIndex(0)), new IrTypeChild(new IrRef("A"), new IrPathIndex(1))],
            IrTypeTree.Children(new IrTuple([new IrElement(Text, []), new IrElement(new IrRef("A"), [])])));
    }

    [Fact]
    public void A_union_variant_shares_its_union_path() =>
        Assert.Equal([new IrTypeChild(Text, null), new IrTypeChild(new IrRef("A"), null)], IrTypeTree.Children(new IrUnion([Text, new IrRef("A")])));

    [Fact]
    public void A_node_outside_the_closed_set_fails_rather_than_being_skipped()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => IrTypeTree.Children(new Stranger()).ToList());
        Assert.Throws<ArgumentOutOfRangeException>(() => IrTypeTree.RenameReferences(new IrArray(new Stranger()), n => n));
    }

    [Fact]
    public void Within_lists_parents_before_children_in_the_ir_order()
    {
        var a = new IrRef("A");
        var tuple = new IrTuple([new IrElement(a, []), new IrElement(Text, [])]);
        var array = new IrArray(tuple);
        var json = new IrJsonText(array);

        Assert.Equal([json, array, tuple, a, Text], IrTypeTree.Within(json));
    }

    [Fact]
    public void References_are_listed_once_each_in_the_order_met()
    {
        var type = new IrUnion([new IrArray(new IrRef("B")), new IrRecord(new IrRef("A"), []), new IrJsonText(new IrRef("B"))]);

        Assert.Equal(["B", "A"], IrTypeTree.References(type));
    }

    [Fact]
    public void Renaming_references_keeps_everything_else_about_each_node()
    {
        var type = new IrArray(new IrTuple([new IrElement(new IrRef("a"), [Because]), new IrElement(new IrRecord(new IrRef("b"), [Because]), [])]))
        {
            ItemsNullableBecause = [Because],
            MinItems = 1,
            MaxItems = 3,
        };

        var renamed = IrTypeTree.RenameReferences(type, name => name.ToUpperInvariant());

        Assert.Equal(
            new IrArray(new IrTuple([new IrElement(new IrRef("A"), [Because]), new IrElement(new IrRecord(new IrRef("B"), [Because]), [])]))
            {
                ItemsNullableBecause = [Because],
                MinItems = 1,
                MaxItems = 3,
            },
            renamed);
    }

    [Fact]
    public void Renaming_reaches_inside_unions_and_json_text() =>
        Assert.Equal(
            new IrUnion([new IrRef("X"), new IrJsonText(new IrRef("X"))]),
            IrTypeTree.RenameReferences(new IrUnion([new IrRef("a"), new IrJsonText(new IrRef("b"))]), _ => "X"));

    /// <summary>A type node the IR doesn't define, as a later version's might be to a reader built before it.</summary>
    private sealed record Stranger : IrType;
}
