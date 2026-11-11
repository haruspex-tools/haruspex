namespace Haruspex.Tests.Inference;

public partial class FunctionTests
{
    [Fact]
    public void Arithmetic_over_float_real_money_and_dates_follows_precedence_and_may_overflow()
    {
        // Arithmetic_over_float_real_money_and_dates_follows_data_type_precedence;
        // Arithmetic_over_float_money_and_dates_overflows_to_an_error_or_to_null_under_the_callers_ansi_warnings_and_arithabort_off.
        var doc = Procedure("""
            SELECT f + n AS a1, f * d AS a2, r + n AS a3, r + m AS a4, r * f AS a5, n / f AS a6,
                   m + m AS b1, m * n AS b2, m / bi AS b3, sm + bi AS b4, m + sm AS b5, m + d AS b6, m * d AS b7, m + 1.5 AS b8, sm + d AS b9, m / d AS b10,
                   dt + 1 AS c1, dt - dt AS c2, sdt + f AS c3, dt + sdt AS c4, 1 + dt AS c5, sdt - dt AS c6, d2 + 1 AS c7, dt * 2 AS c8,
                   -f AS u1, -r AS u2, -m AS u3, -sm AS u4, -ti AS u5, -1.5e0 AS u6, -$2 AS u7, 1.5e0 AS u8, $2.50 AS u9
            FROM dbo.t;
            """);
        Assert.Equal("float arithmetic-overflow", Typed(doc, "a1"));
        Assert.Equal("float arithmetic-overflow", Typed(doc, "a2"));
        Assert.Equal("real nullable-column arithmetic-overflow", Typed(doc, "a3"));
        Assert.Equal("real nullable-column arithmetic-overflow", Typed(doc, "a4"));
        Assert.Equal("float nullable-column arithmetic-overflow", Typed(doc, "a5"));
        Assert.Equal("float arithmetic-overflow", Typed(doc, "a6"));
        Assert.Equal("money arithmetic-overflow", Typed(doc, "b1"));
        Assert.Equal("money arithmetic-overflow", Typed(doc, "b2"));
        Assert.Equal("money nullable-column arithmetic-overflow", Typed(doc, "b3"));
        Assert.Equal("smallmoney nullable-column nullable-column arithmetic-overflow", Typed(doc, "b4"));
        Assert.Equal("money nullable-column arithmetic-overflow", Typed(doc, "b5"));

        // Decimal arithmetic on money as decimal(19,4): + and * cannot overflow within 38 digits, / can divide by zero.
        Assert.Equal("decimal(20,4)", Typed(doc, "b6"));
        Assert.Equal("decimal(29,6)", Typed(doc, "b7"));
        Assert.Equal("numeric(20,4)", Typed(doc, "b8"));
        Assert.Equal("decimal(12,4) nullable-column", Typed(doc, "b9"));
        Assert.Equal("decimal(31,14) arithmetic-overflow", Typed(doc, "b10"));
        Assert.Equal("datetime arithmetic-overflow", Typed(doc, "c1"));
        Assert.Equal("datetime arithmetic-overflow", Typed(doc, "c2"));
        Assert.Equal("smalldatetime nullable-column arithmetic-overflow", Typed(doc, "c3"));
        Assert.Equal("datetime nullable-column arithmetic-overflow", Typed(doc, "c4"));
        Assert.Equal("datetime arithmetic-overflow", Typed(doc, "c5"));
        Assert.Equal("datetime nullable-column arithmetic-overflow", Typed(doc, "c6"));

        // datetime2 + 1 and datetime * 2 are errors on the server.
        Unknown(doc, "c7");
        Unknown(doc, "c8");

        // Negating a float cannot overflow; money's least value can; a tinyint becomes a smallint, which holds -255.
        Assert.Equal("float", Typed(doc, "u1"));
        Assert.Equal("real nullable-column", Typed(doc, "u2"));
        Assert.Equal("money arithmetic-overflow", Typed(doc, "u3"));
        Assert.Equal("smallmoney nullable-column arithmetic-overflow", Typed(doc, "u4"));
        Assert.Equal("smallint", Typed(doc, "u5"));
        Assert.Equal("float", Typed(doc, "u6"));
        Assert.Equal("money", Typed(doc, "u7"));
        Assert.Equal("float", Typed(doc, "u8"));
        Assert.Equal("money", Typed(doc, "u9"));
    }

    [Fact]
    public void Coalesce_over_float_money_and_dates_follows_precedence_where_every_argument_converts()
    {
        // Coalesce_over_float_money_and_dates_follows_data_type_precedence;
        // Coalesce_converting_to_a_narrower_range_overflows_to_null_under_the_callers_ansi_warnings_and_arithabort_off.
        var doc = Procedure("""
            SELECT COALESCE(r, n) AS a1, COALESCE(r, f) AS a2, COALESCE(sm, ti) AS a3, COALESCE(sm, m) AS a4, COALESCE(sm, d) AS a5, COALESCE(m, 1.5) AS a6,
                   COALESCE(sm, n) AS a7, COALESCE(m, bi) AS a8,
                   COALESCE(d21, dt) AS c1, COALESCE(d21, d2) AS c2, COALESCE(d21, sdt) AS c3, COALESCE(d21, da) AS c4, COALESCE(sdt, dt) AS c5, COALESCE(sdt, da) AS c6,
                   IIF(n > 1, f, n) AS i1
            FROM dbo.t;
            """);
        Assert.Equal("real", Typed(doc, "a1"));
        Assert.Equal("float", Typed(doc, "a2"));
        Assert.Equal("smallmoney", Typed(doc, "a3"));
        Assert.Equal("money", Typed(doc, "a4"));
        Assert.Equal("decimal(11,4)", Typed(doc, "a5"));
        Assert.Equal("numeric(19,4)", Typed(doc, "a6"));

        // An int may not fit smallmoney, a bigint money: NULL under the caller's options
        // (A_conversion_that_overflows_is_an_error_by_default_and_null_under_the_callers_options).
        Assert.Equal("smallmoney arithmetic-overflow", Typed(doc, "a7"));
        Assert.Equal("money arithmetic-overflow", Typed(doc, "a8"));
        Assert.Equal("datetime2(3)", Typed(doc, "c1"));
        Assert.Equal("datetime2(3)", Typed(doc, "c2"));
        Assert.Equal("datetime2(1) nullable-column nullable-column", Typed(doc, "c3"));
        Assert.Equal("datetime2(1)", Typed(doc, "c4"));
        Assert.Equal("datetime", Typed(doc, "c5"));

        // A date may not fit smalldatetime (out of range: NULL under the caller's options).
        Assert.Equal("smalldatetime arithmetic-overflow", Typed(doc, "c6"));
        Assert.Equal("float", Typed(doc, "i1"));
    }
}
