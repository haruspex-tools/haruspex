using Haruspex.Ir;

namespace Haruspex.Tests.Ir;

public class IrBoundTests
{
    private static readonly IrFactSource Check = new(IrFactSourceKind.Check, "s.ck");

    [Theory]
    // A lower bound moves up a step, an upper one down, exactly, in decimal: 100 exclusive on decimal(5,2) is 99.99.
    [InlineData("100", 2, false, "99.99")]
    [InlineData("0", 2, true, "0.01")]
    [InlineData("-1.5", 1, true, "-1.4")]
    [InlineData("-0.05", 2, false, "-0.06")]
    [InlineData("0", 0, true, "1")]
    [InlineData("9223372036854775807", 0, false, "9223372036854775806")]
    [InlineData("12.5", 3, false, "12.499")]
    public void An_exclusive_bound_is_the_nearest_value_inside_it_at_the_step(string value, int scale, bool lower, string inside) =>
        Assert.Equal(inside, new IrBound(value, false, Check).InsideAt(scale, lower));

    [Fact]
    public void An_inclusive_bound_is_its_own_value() =>
        Assert.Equal("12.5", new IrBound("12.5", true, Check).InsideAt(2, isLower: true));

    [Fact]
    public void A_bound_finer_than_the_step_has_no_value_inside_at_it() =>
        Assert.Null(new IrBound("0.125", false, Check).InsideAt(2, isLower: true));
}
