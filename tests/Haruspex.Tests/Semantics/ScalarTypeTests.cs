using Haruspex.SqlServer.Semantics;

namespace Haruspex.Tests.Semantics;

/// <summary>
/// Reading a built-in type with its arguments (<see cref="ScalarType"/>): the defaults a declaration applies, <c>float(n)</c>,
/// and arguments a type doesn't take, which give no type rather than a guess.
/// </summary>
public class ScalarTypeTests
{
    [Theory]
    [InlineData("decimal", "decimal(18,0)")]
    [InlineData("char", "char(1)")]
    [InlineData("datetime2", "datetime2(7)")]
    [InlineData("nvarchar(max)", "nvarchar(max)")]
    [InlineData("money", "money")]
    public void A_type_written_without_its_arguments_takes_the_declarations_defaults(string written, string type) =>
        Assert.Equal(type, ScalarType.Parse(written)!.Text);

    [Theory]
    [InlineData("float", "float")]
    [InlineData("float(1)", "real")]
    [InlineData("float(24)", "real")]
    [InlineData("float(25)", "float")]
    [InlineData("float(53)", "float")]
    public void A_float_of_24_bits_or_fewer_is_real(string written, string type) =>
        Assert.Equal(type, ScalarType.Parse(written)!.Text);

    [Theory]
    [InlineData("float(0)")]
    [InlineData("float(54)")]
    [InlineData("int(5)")]
    [InlineData("char(max)")]
    [InlineData("decimal(max)")]
    [InlineData("datetime2(7,2)")]
    public void Arguments_a_type_does_not_take_give_no_type(string written) => Assert.Null(ScalarType.Parse(written));
}
