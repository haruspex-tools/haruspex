namespace Haruspex.Tests.Inference;

// The bitwise operators, the shifts and ||. The types are observed over a grid in BitwiseGridTests, the values in
// ExpressionObservationTests (Bitwise_operators_shifts_and_concatenation_are_null_when_an_operand_is_and_never_overflow).
public partial class ExpressionTests
{
    [Theory]
    [InlineData("t.id & t.small", "int")]
    [InlineData("t.small | t.tiny", "smallint nullable-column")]
    [InlineData("t.big ^ t.id", "bigint nullable-column")]
    [InlineData("CAST(1 AS bit) & t.tiny", "tinyint nullable-column")]
    [InlineData("CAST(1 AS bit) ^ CAST(0 AS bit)", "bit")]
    [InlineData("t.small & 1", "int")]
    [InlineData("t.tiny << 3", "tinyint nullable-column")]
    [InlineData("t.id >> t.big", "int nullable-column")]
    [InlineData("~t.small", "smallint")]
    [InlineData("~t.tiny", "tinyint nullable-column")]
    [InlineData("~CAST(0 AS bit)", "bit")]
    public void A_bitwise_operator_or_a_shift_is_typed_by_its_operands_and_never_overflows(string expression, string expected) =>
        Assert.Equal(expected, Typed(Procedure($"SELECT {expression} AS x FROM dbo.t AS t;"), "x"));

    [Theory]
    // A string beside an integer is converted to its type, which may overflow, as a CAST may (Conversions.Table).
    [InlineData("t.code & t.id", "int nullable-column arithmetic-overflow")]
    [InlineData("t.tiny | t.short", "tinyint nullable-column nullable-column arithmetic-overflow")]
    public void A_string_beside_an_integer_is_converted_to_its_type(string expression, string expected) =>
        Assert.Equal(expected, Typed(Procedure($"SELECT {expression} AS x FROM dbo.t AS t;"), "x"));

    [Theory]
    [InlineData("t.label || N'!'", "nvarchar(11)")]
    [InlineData("t.code || t.short", "varchar(9) nullable-column nullable-column")]
    [InlineData("t.short || t.id", "varchar(17) nullable-column")]
    [InlineData("t.label || t.amount", "nvarchar(51) nullable-column")]
    [InlineData("t.born || t.label", "nvarchar(50)")]
    [InlineData("t.label || t.ratio", "nvarchar(33) nullable-column")]
    [InlineData("CAST(t.label AS nvarchar(4000)) || t.label", "nvarchar(4000)")]
    [InlineData("CAST(t.short AS varchar(max)) || t.label", "nvarchar(max) nullable-column")]
    public void Concatenation_with_two_bars_is_a_varying_string_as_long_as_both_and_null_when_either_is(string expression, string expected) =>
        Assert.Equal(expected, Typed(Procedure($"SELECT {expression} AS x FROM dbo.t AS t;"), "x"));

    [Theory]
    [InlineData("t.amount & t.id")]
    [InlineData("t.ratio | 1")]
    [InlineData("t.short ^ t.code")]
    [InlineData("t.id << CAST(1 AS bit)")]
    [InlineData("t.amount >> 1")]
    [InlineData("t.short << 1")]
    [InlineData("t.id || t.small")]
    [InlineData("CAST(1 AS bit) || t.label")]
    [InlineData("~t.amount")]
    [InlineData("~t.label")]
    public void A_pair_sql_server_refuses_is_unknown(string expression)
    {
        var doc = Procedure($"SELECT {expression} AS x FROM dbo.t AS t;");

        Assert.Equal("unknown: unsupported-expression", Sql(doc, "x"));
        Assert.Contains("SQL Server refuses", Assert.Single(doc.Unknowns).Detail, StringComparison.Ordinal);
    }
}
