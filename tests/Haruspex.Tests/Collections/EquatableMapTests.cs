using Haruspex.Collections;

namespace Haruspex.Tests.Collections;

/// <summary>
/// A map that compares by its entries, whatever order they were added in, so that the records holding one compare by value;
/// its keys compare by their own equality, so a key that ignores case is a type that says so.
/// </summary>
public class EquatableMapTests
{
    /// <summary>A key whose own equality ignores case, as a SQL name's does.</summary>
    private sealed record Name(string Text)
    {
        public bool Equals(Name? other) => other is not null && StringComparer.OrdinalIgnoreCase.Equals(Text, other.Text);

        public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Text);
    }

    /// <summary>Maps of up to three entries over two keys and two values, in every order of adding them, each built apart.</summary>
    private static List<EquatableMap<string, int>> Sample()
    {
        (string, int)[][] entries =
        [
            [], [("a", 1)], [("a", 2)], [("b", 1)], [("a", 1), ("b", 2)], [("b", 2), ("a", 1)], [("a", 1), ("b", 1)], [("a", 2), ("b", 2), ("a", 1)],
        ];
        return [.. entries.Select(e => e.ToEquatableMap(p => p.Item1, p => p.Item2)), .. entries.Select(e => e.Reverse().ToEquatableMap(p => p.Item1, p => p.Item2))];
    }

    [Fact]
    public void Equality_is_reflexive_symmetric_and_transitive_and_equal_maps_hash_equal()
    {
        var sample = Sample();
        foreach (var a in sample)
        {
            Assert.Equal(a, a);
            foreach (var b in sample)
            {
                Assert.Equal(a.Equals(b), b.Equals(a));
                if (a.Equals(b))
                    Assert.Equal(a.GetHashCode(), b.GetHashCode());
                foreach (var c in sample.Where(c => a.Equals(b) && b.Equals(c)))
                    Assert.Equal(a, c);
            }
        }
    }

    [Fact]
    public void Maps_built_in_different_orders_are_equal_and_hash_equal()
    {
        var forwards = new[] { ("a", 1), ("b", 2), ("c", 3) }.ToEquatableMap(p => p.Item1, p => p.Item2);
        var backwards = Empty().SetItem("c", 3).SetItem("b", 2).SetItem("a", 1);

        Assert.NotSame(forwards, backwards);
        Assert.Equal(forwards, backwards);
        Assert.True(forwards == backwards);
        Assert.Equal(forwards.GetHashCode(), backwards.GetHashCode());
    }

    [Fact]
    public void A_key_or_a_value_that_differs_makes_maps_different()
    {
        var map = Empty().SetItem("a", 1);

        Assert.NotEqual(map, Empty().SetItem("a", 2));
        Assert.NotEqual(map, Empty().SetItem("b", 1));
        Assert.NotEqual(map, map.SetItem("b", 1));
        Assert.Equal(map, map.SetItem("b", 1).Remove("b"));
    }

    [Fact]
    public void Which_keys_are_the_same_is_the_keys_own_equality()
    {
        // Keys compare by their type's equality, so a key that ignores case makes one entry of two spellings, and two maps
        // never disagree about which keys are the same: the map takes no comparer.
        var map = Map().SetItem(new Name("Lines"), 1).SetItem(new Name("LINES"), 2);

        Assert.Single(map);
        Assert.Equal(2, map[new Name("lines")]);
        Assert.Equal(map, Map().SetItem(new Name("lines"), 2));
        Assert.Equal(map.GetHashCode(), Map().SetItem(new Name("lines"), 2).GetHashCode());
    }

    [Fact]
    public void A_missing_map_equals_only_another_missing_one()
    {
        EquatableMap<string, int>? missing = null;
        EquatableMap<string, int>? empty = [];

        Assert.True(missing == null);
        Assert.False(missing == empty);
        Assert.False(empty == missing);
        Assert.False(empty.Equals(null));
    }

    [Fact]
    public void A_record_holding_one_compares_by_value()
    {
        var a = new Holder(Empty().SetItem("a", 1).SetItem("b", 2));
        var b = new Holder(Empty().SetItem("b", 2).SetItem("a", 1));

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    private sealed record Holder(EquatableMap<string, int> Map);

    private static EquatableMap<string, int> Empty() => [];

    private static EquatableMap<Name, int> Map() => [];
}
