namespace Haruspex.Tests.Inference;

public partial class FunctionTests
{
    [Fact]
    public void Parsename_is_nvarchar_128_always_out_of_range_and_overflows_from_a_bigint_part()
    {
        // Parsename_is_nvarchar_128_and_null_for_a_part_it_cannot_find_or_a_name_it_cannot_read.
        var doc = Procedure("""
            SELECT PARSENAME(s, 1) AS a, PARSENAME(ns, n) AS b, PARSENAME(REPLACE(c, '-', '.'), ti) AS c, PARSENAME(nm, bi) AS d,
                   PARSENAME('a.b.c', 1) AS e, PARSENAME(n, 1) AS f, PARSENAME(s, s) AS g
            FROM dbo.t;
            """);
        Assert.Equal("nvarchar(128) out-of-range", Typed(doc, "a"));
        Assert.Equal("nvarchar(128) nullable-column out-of-range", Typed(doc, "b"));
        Assert.Equal("nvarchar(128) out-of-range", Typed(doc, "c"));
        Assert.Equal("nvarchar(128) nullable-column nullable-column out-of-range arithmetic-overflow", Typed(doc, "d"));
        Assert.Equal("nvarchar(128) out-of-range", Typed(doc, "e"));
        Unknown(doc, "f");
        Unknown(doc, "g");
    }

    [Fact]
    public void Db_name_is_nvarchar_128_never_null_without_an_id_and_out_of_range_with_one()
    {
        // Db_name_is_nvarchar_128_never_null_for_the_current_database_and_null_for_an_id_the_caller_cannot_see.
        var doc = Procedure("SELECT DB_NAME() AS a, DB_NAME(n) AS b, DB_NAME(v) AS c, DB_NAME(1) AS d, DB_NAME(bi) AS e, DB_NAME(s) AS f FROM dbo.t;");
        Assert.Equal("nvarchar(128)", Typed(doc, "a"));
        Assert.Equal("nvarchar(128) out-of-range", Typed(doc, "b"));
        Assert.Equal("nvarchar(128) nullable-column out-of-range", Typed(doc, "c"));
        Assert.Equal("nvarchar(128) out-of-range", Typed(doc, "d"));
        Assert.Equal("nvarchar(128) nullable-column out-of-range arithmetic-overflow", Typed(doc, "e"));
        Unknown(doc, "f");
    }
}
