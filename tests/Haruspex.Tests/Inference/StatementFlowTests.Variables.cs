using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

public partial class StatementFlowTests
{
    [Fact]
    public void A_variable_has_its_declared_type_and_is_null_until_assigned()
    {
        var doc = Procedure("DECLARE @n nvarchar(5);\nSELECT @n AS n;");

        Assert.Equal("nvarchar(5)", Scalar(Field(doc, "n")).Sql);
        Assert.Equal([IrNullRule.Variable], Rules(Field(doc, "n")));
    }

    [Fact]
    public void A_variable_assigned_a_value_that_is_never_null_is_never_null()
    {
        var doc = Procedure("DECLARE @n int = 0;\nDECLARE @m int;\nSET @m = 1;\nSELECT @n AS n, @m AS m;");

        Assert.False(Field(doc, "n").Nullable);
        Assert.False(Field(doc, "m").Nullable);
    }

    [Fact]
    public void A_select_that_may_assign_nothing_leaves_a_variable_as_it_was()
    {
        var doc = Procedure("DECLARE @n int;\nSELECT @n = t.code FROM dbo.t AS t;\nSELECT @n AS n;");

        Assert.Equal([IrNullRule.Variable], Rules(Field(doc, "n")));
    }

    [Fact]
    public void A_variable_assigned_on_some_paths_only_may_be_null()
    {
        var doc = Procedure("DECLARE @n int;\nIF @x = 1 SET @n = 1;\nSELECT @n AS n;", "@x int");

        Assert.True(Field(doc, "n").Nullable);
    }

    [Fact]
    public void A_parameter_read_in_a_procedure_has_its_type_and_may_be_null()
    {
        var doc = Procedure("SELECT @x AS x;", "@x tinyint");

        Assert.Equal("tinyint", Scalar(Field(doc, "x")).Sql);
        Assert.Equal([IrNullRule.Parameter], Rules(Field(doc, "x")));
    }

    [Fact]
    public void An_integer_literal_is_a_non_null_int()
    {
        var doc = Procedure("SELECT 0 AS zero, 2147483647 AS biggest;");

        Assert.Equal("int", Scalar(Field(doc, "zero")).Sql);
        Assert.False(Field(doc, "zero").Nullable);
        Assert.Equal("int", Scalar(Field(doc, "biggest")).Sql);
    }

    [Fact]
    public void A_larger_integer_literal_is_numeric_of_its_digits()
    {
        // Observed: A_literals_type_is_its_own_digits_or_characters.
        var doc = Procedure("SELECT 2147483648 AS big;");
        Assert.Equal("numeric(10,0)", Scalar(Field(doc, "big")).Sql);
        Assert.False(Field(doc, "big").Nullable);
    }

    [Fact]
    public void A_cast_of_a_string_to_a_string_type_has_that_type_and_the_operands_nullability()
    {
        var doc = Procedure("SELECT CAST(t.name AS nvarchar(200)) AS a, CAST(u.name AS varchar(3)) AS b FROM dbo.t AS t JOIN dbo.u AS u ON u.t_id = t.id;");

        Assert.Equal(200, Scalar(Field(doc, "a")).Facets!.Length!.Value);
        Assert.True(Field(doc, "a").Nullable);
        Assert.Equal("varchar(3)", Scalar(Field(doc, "b")).Sql);
        Assert.False(Field(doc, "b").Nullable);
    }

    [Theory]
    [InlineData("CAST(t.name AS varchar)", "varchar(30)")]
    [InlineData("CONVERT(nvarchar, t.name)", "nvarchar(30)")]
    [InlineData("CAST(t.name AS char)", "char(30)")]
    public void A_cast_to_a_string_type_without_a_length_is_30_long(string expression, string sql)
    {
        // Observed: A_string_type_without_a_length_is_30_long_in_a_cast_and_1_in_a_declaration.
        var scalar = Scalar(Field(Procedure($"SELECT {expression} AS a FROM dbo.t AS t;"), "a"));

        Assert.Equal(sql, scalar.Sql);
        Assert.Equal(30, scalar.Facets!.Length!.Value);
    }

    [Fact]
    public void A_cast_to_a_type_not_modelled_is_not_modelled_yet() =>
        // Other casts are typed (ExpressionTests); a binary type is not modelled at all.
        AssertUnknown(Procedure("SELECT CAST(t.id AS varbinary(5)) AS a FROM dbo.t AS t;"), "results[0][].a", IrUnknownReason.UnsupportedExpression);
}
