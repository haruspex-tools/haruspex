namespace Haruspex.Tests.Inference;

public partial class FunctionTests
{
    [Fact]
    public void The_trigonometric_functions_are_float_and_null_outside_their_domain_where_they_have_one()
    {
        // The_trigonometric_functions_and_pi_are_float_and_null_only_outside_their_domain_under_the_callers_options.
        var doc = Procedure("SELECT SIN(n) AS a, ASIN(d) AS b, ASIN(CAST(1 AS bit)) AS c, COT(f) AS d2, COT(n) AS e, COT(CAST(0 AS bit)) AS e2, ATN2(n, ti) AS f2, PI() AS g, COS(s) AS h FROM dbo.t;");
        Assert.Equal("float", Typed(doc, "a"));
        Assert.Equal("float domain-error", Typed(doc, "b"));
        Assert.Equal("float", Typed(doc, "c"));
        Assert.Equal("float arithmetic-overflow domain-error", Typed(doc, "d2"));
        Assert.Equal("float domain-error", Typed(doc, "e"));
        Assert.Equal("float domain-error", Typed(doc, "e2"));
        Assert.Equal("float domain-error", Typed(doc, "f2"));
        Assert.Equal("float", Typed(doc, "g"));
        Unknown(doc, "h");
    }

    [Fact]
    public void Degrees_and_radians_keep_an_integers_type_or_a_floats_and_leave_a_decimal_unknown()
    {
        // Degrees_and_radians_keep_an_integers_type_or_a_floats_and_degrees_may_overflow.
        var doc = Procedure("SELECT DEGREES(ti) AS a, DEGREES(n) AS b, RADIANS(bi) AS c, DEGREES(f) AS d2, RADIANS(r) AS e, DEGREES(d) AS f2 FROM dbo.t;");
        Assert.Equal("int", Typed(doc, "a"));
        Assert.Equal("int arithmetic-overflow", Typed(doc, "b"));
        Assert.Equal("bigint nullable-column", Typed(doc, "c"));
        Assert.Equal("float arithmetic-overflow", Typed(doc, "d2"));
        Assert.Equal("float nullable-column", Typed(doc, "e"));
        Unknown(doc, "f2");
    }

    [Fact]
    public void Greatest_and_least_of_one_kind_are_null_only_when_every_value_may_be()
    {
        // Greatest_and_least_skip_nulls_and_cap_a_max_string_at_8000_bytes_which_a_longer_value_fails.
        var doc = Procedure("""
            SELECT GREATEST(n, ti) AS a, LEAST(v, bi) AS b, GREATEST(c, s) AS c2, LEAST(ns, vm) AS d2, GREATEST(f, r) AS e,
                   GREATEST(da, da) AS f2, GREATEST(d, n) AS g, LEAST(v, v) AS h, GREATEST(c, c) AS k, LEAST(c, nc) AS m2
            FROM dbo.t;
            """);
        Assert.Equal("int", Typed(doc, "a"));
        Assert.Equal("bigint nullable-column nullable-column", Typed(doc, "b"));
        Assert.Equal("varchar(10)", Typed(doc, "c2"));
        Assert.Equal("nvarchar(4000) nullable-column nullable-column", Typed(doc, "d2"));
        Assert.Equal("float", Typed(doc, "e"));
        Assert.Equal("date", Typed(doc, "f2"));
        Unknown(doc, "g");
        Assert.Equal("int nullable-column", Typed(doc, "h"));

        // Fixed-length strings stay fixed when every value is of one type; of different types they are not observed.
        Assert.Equal("char(5)", Typed(doc, "k"));
        Unknown(doc, "m2");
    }

    [Fact]
    public void Isjson_is_an_int_0_or_1_null_as_its_string_is_and_newid_a_uniqueidentifier_never_null()
    {
        // Isjson_is_an_int_null_for_null_and_newid_a_uniqueidentifier_never_null, Isdate_and_isjson_give_0_or_1_and_isjson_null_for_null.
        var doc = Procedure("SELECT ISJSON(doc) AS a, ISJSON(s) AS b, NEWID() AS c, ISJSON(n) AS d FROM dbo.t;");
        Assert.Equal("int nullable-column", Typed(doc, "a"));
        Assert.Equal(("0", "1", "ISJSON"), Range(doc, "a"));
        Assert.Equal("int", Typed(doc, "b"));
        Assert.Equal("uniqueidentifier", Typed(doc, "c"));
        Unknown(doc, "d");
    }
}
