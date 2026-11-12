using Haruspex.Ir;

namespace Haruspex.Tests.Ir;

/// <summary>
/// A sequence that compares by its items, so that the records holding one compare by value. The laws equality must keep
/// over shapes as a whole are in <c>ShapeEqualityLawTests</c>; these are the sequence's own.
/// </summary>
public class EquatableArrayTests
{
    private static readonly IrNullReason Column = new(IrNullRule.NullableColumn, "a.t.c");
    private static readonly IrNullReason Join = new(IrNullRule.OuterJoin, "a.sql:3:1");

    [Fact]
    public void Sequences_built_apart_with_equal_items_are_equal_and_hash_equal()
    {
        EquatableArray<IrNullReason> a = [Column, Join];
        EquatableArray<IrNullReason> b = [.. new List<IrNullReason> { new(IrNullRule.NullableColumn, "a.t.c"), new(IrNullRule.OuterJoin, "a.sql:3:1") }];

        Assert.NotSame(a, b);
        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Every_item_counts_in_the_hash_first_middle_and_last()
    {
        // A hash that left an item out would still be lawful (equal sequences would hash equal), but every sequence differing
        // only there would share a bucket. HashCode is seeded per process, so two different sequences may hash alike by
        // chance, about once in 2^32 runs.
        var other = new IrNullReason(IrNullRule.NoRows, "a.sql:9:1");
        EquatableArray<IrNullReason> three = [Column, Join, Column];
        EquatableArray<IrNullReason> first = [other, Join, Column];
        EquatableArray<IrNullReason> middle = [Column, other, Column];
        EquatableArray<IrNullReason> last = [Column, Join, other];

        Assert.NotEqual(three.GetHashCode(), first.GetHashCode());
        Assert.NotEqual(three.GetHashCode(), middle.GetHashCode());
        Assert.NotEqual(three.GetHashCode(), last.GetHashCode());
    }

    [Fact]
    public void Order_and_length_matter()
    {
        EquatableArray<IrNullReason> both = [Column, Join];

        Assert.NotEqual(both, [Join, Column]);
        Assert.NotEqual(both, [Column]);
        Assert.NotEqual(both, [Column, Join, Join]);
    }

    [Fact]
    public void A_record_holding_one_compares_by_value()
    {
        // The reason this type exists: with IReadOnlyList, these two fields were different.
        var a = new IrField("total", new IrScalar(IrScalarKind.Number, IrFormat.Int32, "int"), IrPresence.Always, [Column]);
        var b = new IrField("total", new IrScalar(IrScalarKind.Number, IrFormat.Int32, "int"), IrPresence.Always, [new(IrNullRule.NullableColumn, "a.t.c")]);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Every_empty_sequence_is_equal_whatever_built_it()
    {
        EquatableArray<IrNullReason> literal = [];
        EquatableArray<IrNullReason> spread = [.. Array.Empty<IrNullReason>()];

        Assert.Equal(literal, spread);
        Assert.Empty(literal);
        Assert.Equal(literal.GetHashCode(), spread.GetHashCode());
    }

    [Fact]
    public void A_missing_sequence_equals_only_another_missing_one()
    {
        EquatableArray<IrNullReason>? missing = null;
        EquatableArray<IrNullReason>? empty = [];

        Assert.True(missing == null);
        Assert.False(missing == empty);
        Assert.False(empty == missing);
        Assert.False(empty.Equals(null));
    }

    [Fact]
    public void It_reads_as_a_list()
    {
        EquatableArray<IrNullReason> both = [Column, Join];

        Assert.Equal(2, both.Count);
        Assert.Equal(Join, both[1]);
        Assert.Equal([Column, Join], both.ToList());
        Assert.Equal([Column, Join], both.Items);
    }
}
