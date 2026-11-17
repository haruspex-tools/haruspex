namespace Haruspex.Tests.Inference;

public partial class FunctionTests
{
    [Fact]
    public void Datetrunc_and_date_bucket_keep_the_dates_type_and_are_null_as_the_date_or_the_width_is()
    {
        // Datetrunc_and_date_bucket_keep_the_dates_type_and_are_null_only_for_a_null_date_or_width. The origin's NULL doesn't count.
        var doc = Procedure("""
            SELECT DATETRUNC(month, da) AS a, DATETRUNC(minute, tm) AS b, DATETRUNC(day, dto) AS c, DATE_BUCKET(week, 1, d2) AS d,
                   DATE_BUCKET(day, v, sdt) AS e, DATE_BUCKET(hour, 2, dt, dt) AS f, DATE_BUCKET(day, 2, da, d2) AS g, DATETRUNC(month, s) AS h
            FROM dbo.t;
            """);
        Assert.Equal("date", Typed(doc, "a"));
        Assert.Equal("time(2) nullable-column", Typed(doc, "b"));
        Assert.Equal("datetimeoffset(4) nullable-column", Typed(doc, "c"));
        Assert.Equal("datetime2(3)", Typed(doc, "d"));
        Assert.Equal("smalldatetime nullable-column nullable-column", Typed(doc, "e"));
        Assert.Equal("datetime", Typed(doc, "f"));
        Unknown(doc, "g");
        Unknown(doc, "h");
    }

    [Fact]
    public void Switchoffset_and_todatetimeoffset_are_datetimeoffset_of_the_dates_fractional_digits()
    {
        // Switchoffset_and_todatetimeoffset_are_datetimeoffset_of_the_dates_fractional_digits.
        var doc = Procedure("""
            SELECT SWITCHOFFSET(da, '+01:00') AS a, SWITCHOFFSET(dt, n) AS b, SWITCHOFFSET(sdt, '+01:00') AS c, SWITCHOFFSET(dto, s) AS d,
                   TODATETIMEOFFSET(d2, '+01:00') AS e, TODATETIMEOFFSET(tm, ns) AS f, SWITCHOFFSET(s, '+01:00') AS g
            FROM dbo.t;
            """);
        Assert.Equal("datetimeoffset(7)", Typed(doc, "a"));
        Assert.Equal("datetimeoffset(3) arithmetic-overflow", Typed(doc, "b"));
        Assert.Equal("datetimeoffset(0) nullable-column", Typed(doc, "c"));
        Assert.Equal("datetimeoffset(4) nullable-column", Typed(doc, "d"));
        Assert.Equal("datetimeoffset(3)", Typed(doc, "e"));
        Assert.Equal("datetimeoffset(2) nullable-column nullable-column", Typed(doc, "f"));
        Unknown(doc, "g");

        // A number of minutes is converted to smallint, which an int may overflow, but a tinyint or a literal can't.
        var narrow = Procedure("SELECT SWITCHOFFSET(dto, ti) AS a, TODATETIMEOFFSET(d2, -300) AS b, DATE_BUCKET(day, d, da) AS c FROM dbo.t;");
        Assert.Equal("datetimeoffset(4) nullable-column", Typed(narrow, "a"));
        Assert.Equal("datetimeoffset(3)", Typed(narrow, "b"));
        Assert.Equal("date arithmetic-overflow", Typed(narrow, "c"));
    }

    [Fact]
    public void At_time_zone_is_datetimeoffset_of_the_dates_fractional_digits_null_only_as_the_date_or_zone_is()
    {
        // At_time_zone_is_datetimeoffset_of_the_dates_fractional_digits_and_null_only_for_a_null_date_or_zone. A date past the
        // range is held at its end, so nothing overflows; a date, time, string or number is refused, so not modelled.
        var doc = Procedure("""
            SELECT d2 AT TIME ZONE 'UTC' AS a, dt AT TIME ZONE ns AS b, sdt AT TIME ZONE 'UTC' AS c, dto AT TIME ZONE s AS d,
                   (d21 AT TIME ZONE 'UTC') AT TIME ZONE 'GMT Standard Time' AS e, da AT TIME ZONE 'UTC' AS f, tm AT TIME ZONE 'UTC' AS g,
                   s AT TIME ZONE 'UTC' AS h, d2 AT TIME ZONE n AS i
            FROM dbo.t;
            """);
        Assert.Equal("datetimeoffset(3)", Typed(doc, "a"));
        Assert.Equal("datetimeoffset(3) nullable-column", Typed(doc, "b"));
        Assert.Equal("datetimeoffset(0) nullable-column", Typed(doc, "c"));
        Assert.Equal("datetimeoffset(4) nullable-column", Typed(doc, "d"));
        Assert.Equal("datetimeoffset(1) nullable-column", Typed(doc, "e"));
        Unknown(doc, "f");
        Unknown(doc, "g");
        Unknown(doc, "h");
        Unknown(doc, "i");
    }

    [Fact]
    public void Isdate_is_an_int_0_or_1_never_null_even_of_a_null()
    {
        // Isdate_is_an_int_never_null, Isdate_and_isjson_give_0_or_1_and_isjson_null_for_null.
        var doc = Procedure("SELECT ISDATE(ns) AS a, ISDATE(NULL) AS b, ISDATE(dt) AS c, ISDATE(da) AS d FROM dbo.t;");
        Assert.Equal("int", Typed(doc, "a"));
        Assert.Equal(("0", "1", "ISDATE"), Range(doc, "a"));
        Assert.Equal(("0", "1", "ISDATE"), Range(doc, "b"));
        Assert.Equal("int", Typed(doc, "b"));
        Assert.Equal("int", Typed(doc, "c"));
        Unknown(doc, "d");
    }
}
