using Haruspex.SqlServer.Semantics;

namespace Haruspex.Tests.Semantics;

/// <summary>
/// The bound lattice's order, meet and join (<see cref="Bounds"/>), at both ends, over every pair and triple of a small sample
/// of bounds: the values around zero, each inclusive and exclusive.
/// </summary>
public class BoundsTests
{
    private static readonly Bound[] Sample =
    [
        .. new[] { "-1", "0", "0.5", "1" }.SelectMany(v => new[] { new Bound(ExactDecimal.Parse(v), true), new Bound(ExactDecimal.Parse(v), false) }),
    ];

    public static TheoryData<BoundSide> Sides { get; } = [BoundSide.Lower, BoundSide.Upper];

    /// <summary>Each bound as a witness-carrying class, so that which side the meet or join kept can be seen.</summary>
    private sealed record Witnessed(Bound Bound, string Name);

    [Fact]
    public void A_higher_lower_bound_and_a_lower_upper_bound_allow_fewer_values()
    {
        var zero = new Bound(ExactDecimal.Parse("0"), true);
        var one = new Bound(ExactDecimal.Parse("1"), true);
        Assert.True(Bounds.Below(one, zero, BoundSide.Lower));
        Assert.False(Bounds.Below(zero, one, BoundSide.Lower));
        Assert.True(Bounds.Below(zero, one, BoundSide.Upper));
        Assert.False(Bounds.Below(one, zero, BoundSide.Upper));
    }

    [Theory]
    [MemberData(nameof(Sides))]
    public void At_the_same_value_an_exclusive_bound_is_below_an_inclusive_one(BoundSide side)
    {
        var inclusive = new Bound(ExactDecimal.Parse("0"), true);
        var exclusive = new Bound(ExactDecimal.Parse("0"), false);
        Assert.True(Bounds.Below(exclusive, inclusive, side));
        Assert.False(Bounds.Below(inclusive, exclusive, side));
        Assert.Equal(exclusive, Bounds.Tighter(inclusive, exclusive, side));
        Assert.Equal("inclusive", Bounds.Looser(new Witnessed(inclusive, "inclusive"), new Witnessed(exclusive, "exclusive"), side, w => w.Bound)!.Name);
    }

    [Theory]
    [MemberData(nameof(Sides))]
    public void The_order_is_a_total_order(BoundSide side)
    {
        foreach (var a in Sample)
        {
            Assert.True(Bounds.Below(a, a, side));
            foreach (var b in Sample)
            {
                Assert.True(Bounds.Below(a, b, side) || Bounds.Below(b, a, side));
                if (Bounds.Below(a, b, side) && Bounds.Below(b, a, side))
                    Assert.Equal(a, b);
                foreach (var c in Sample)
                    Assert.True(!(Bounds.Below(a, b, side) && Bounds.Below(b, c, side)) || Bounds.Below(a, c, side));
            }
        }
    }

    [Theory]
    [MemberData(nameof(Sides))]
    public void The_meet_is_the_greatest_lower_bound_and_the_join_the_least_upper_bound(BoundSide side)
    {
        foreach (var a in Sample)
        {
            foreach (var b in Sample)
            {
                var meet = Bounds.Tighter(a, b, side)!.Value;
                var join = Bounds.Looser(new Witnessed(a, "a"), new Witnessed(b, "b"), side, w => w.Bound)!.Bound;
                Assert.True(Bounds.Below(meet, a, side) && Bounds.Below(meet, b, side));
                Assert.True(Bounds.Below(a, join, side) && Bounds.Below(b, join, side));
                foreach (var c in Sample)
                {
                    Assert.True(!(Bounds.Below(c, a, side) && Bounds.Below(c, b, side)) || Bounds.Below(c, meet, side));
                    Assert.True(!(Bounds.Below(a, c, side) && Bounds.Below(b, c, side)) || Bounds.Below(join, c, side));
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Sides))]
    public void Of_two_bounds_that_say_the_same_the_meet_keeps_the_right_witness_and_the_join_the_left(BoundSide side)
    {
        var (left, right) = (new Witnessed(Sample[0], "left"), new Witnessed(Sample[0], "right"));
        Assert.Equal("right", Bounds.Tighter(left, right, side, w => w.Bound)!.Name);
        Assert.Equal("left", Bounds.Looser(left, right, side, w => w.Bound)!.Name);
    }

    [Theory]
    [MemberData(nameof(Sides))]
    public void No_bound_is_the_top_so_the_meet_keeps_the_other_and_the_join_has_none(BoundSide side)
    {
        var one = new Witnessed(Sample[0], "one");
        Assert.Same(one, Bounds.Tighter(one, null, side, w => w.Bound));
        Assert.Same(one, Bounds.Tighter(null, one, side, w => w.Bound));
        Assert.Null(Bounds.Looser(one, null, side, w => w.Bound));
        Assert.Null(Bounds.Looser(one, one, side, _ => null));
    }

    [Fact]
    public void A_value_is_admitted_by_a_bound_it_does_not_pass()
    {
        var zero = ExactDecimal.Parse("0");
        Assert.True(Bounds.Admits(new Bound(zero, true), zero, BoundSide.Lower));
        Assert.False(Bounds.Admits(new Bound(zero, false), zero, BoundSide.Lower));
        Assert.True(Bounds.Admits(new Bound(zero, false), ExactDecimal.Parse("0.5"), BoundSide.Lower));
        Assert.False(Bounds.Admits(new Bound(zero, true), ExactDecimal.Parse("0.5"), BoundSide.Upper));
        Assert.True(Bounds.Admits(new Bound(zero, true), ExactDecimal.Parse("-1"), BoundSide.Upper));
    }
}
