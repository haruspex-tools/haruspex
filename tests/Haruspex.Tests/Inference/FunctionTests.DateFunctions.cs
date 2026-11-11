using Haruspex.Ir;

namespace Haruspex.Tests.Inference;

public partial class FunctionTests
{
    [Fact]
    public void Date_functions_are_typed_by_their_kind_and_null_when_an_argument_is()
    {
        // Date_functions_are_typed_by_their_kind_and_dateadd_by_its_dates_type;
        // A_date_function_is_null_when_an_argument_is_but_eomonth_ignores_a_null_offset;
        // A_date_overflow_is_an_error_even_under_the_callers_ansi_warnings_and_arithabort_off (so no arithmetic-overflow).
        var doc = Procedure("""
            SELECT DATEDIFF(day, da, dt) AS a1, DATEDIFF_BIG(second, sdt, d2) AS a2, DATEADD(day, 1, da) AS a3, DATEADD(day, v, dt) AS a4,
                   DATEADD(day, 1, d21) AS a5, DATEADD(minute, 1, sdt) AS a6, DATEADD(hour, n, dto) AS a7, DATEADD(hour, 1, tm) AS a8,
                   DATEADD(day, 1, s) AS a9, DATEADD(day, 1, n) AS a10, DATEDIFF(day, 0, dt) AS a11,
                   DATEPART(year, da) AS b1, YEAR(sdt) AS b2, MONTH(dt) AS b3, DAY(d2) AS b4, DATENAME(month, da) AS b5, EOMONTH(sdt) AS b6,
                   EOMONTH(da, v) AS b7, DATEPART(weekday, s) AS b8
            FROM dbo.t;
            """);
        Assert.Equal("int", Typed(doc, "a1"));
        Assert.Equal("bigint nullable-column", Typed(doc, "a2"));
        Assert.Equal("date", Typed(doc, "a3"));
        Assert.Equal("datetime nullable-column", Typed(doc, "a4"));
        Assert.Equal("datetime2(1) nullable-column", Typed(doc, "a5"));
        Assert.Equal("smalldatetime nullable-column", Typed(doc, "a6"));
        Assert.Equal("datetimeoffset(4) nullable-column", Typed(doc, "a7"));
        Assert.Equal("time(2) nullable-column", Typed(doc, "a8"));
        Assert.Equal("datetime", Typed(doc, "a9"));
        Assert.Equal("datetime", Typed(doc, "a10"));
        Assert.Equal("int", Typed(doc, "a11"));
        Assert.Equal("int", Typed(doc, "b1"));
        Assert.Equal("int nullable-column", Typed(doc, "b2"));
        Assert.Equal("int", Typed(doc, "b3"));
        Assert.Equal("int", Typed(doc, "b4"));
        Assert.Equal("nvarchar(30)", Typed(doc, "b5"));
        Assert.Equal("date nullable-column", Typed(doc, "b6"));

        // A NULL month offset is taken as 0.
        Assert.Equal("date", Typed(doc, "b7"));
        Assert.Equal("int", Typed(doc, "b8"));
    }

    [Fact]
    public void Datename_is_not_a_closed_set()
    {
        // It depends on the session's language.
        var doc = Procedure("SELECT DATENAME(month, da) AS m FROM dbo.t;");
        Assert.Null(((IrScalar)Field(doc, "m").Type).Facets?.Values);
    }

    [Fact]
    public void A_date_function_over_a_type_it_does_not_take_is_not_typed()
    {
        var doc = Procedure("SELECT DATEADD(day, 1, f) AS a, DATEADD(day, 1, d) AS b, YEAR(m) AS c FROM dbo.t;");
        Unknown(doc, "a");
        Unknown(doc, "b");
        Unknown(doc, "c");
    }

    [Fact]
    public void Fromparts_are_of_their_type_and_precision_and_null_when_a_part_is()
    {
        var doc = Procedure("""
            SELECT DATEFROMPARTS(2026, 1, 1) AS a, DATEFROMPARTS(2026, v, 1) AS b, DATETIME2FROMPARTS(2026, 1, 1, 1, 1, 1, 1, 3) AS c, DATETIMEFROMPARTS(2026, 1, 1, 1, 1, 1, v) AS d,
                   SMALLDATETIMEFROMPARTS(2026, 1, 1, 1, 1) AS e, TIMEFROMPARTS(1, 1, 1, 1, 2) AS f, DATETIMEOFFSETFROMPARTS(2026, 1, 1, 1, 1, 1, 1, 1, 0, 3) AS g,
                   DATETIME2FROMPARTS(2026, 1, 1, 1, 1, 1, 1, n) AS h
            FROM dbo.t;
            """);
        Assert.Equal("date", Typed(doc, "a"));
        Assert.Equal("date nullable-column", Typed(doc, "b"));
        Assert.Equal("datetime2(3)", Typed(doc, "c"));
        Assert.Equal("datetime nullable-column", Typed(doc, "d"));
        Assert.Equal("smalldatetime", Typed(doc, "e"));
        Assert.Equal("time(2)", Typed(doc, "f"));
        Assert.Equal("datetimeoffset(3)", Typed(doc, "g"));

        // The precision must be a constant.
        Unknown(doc, "h");
    }

    [Fact]
    public void The_current_date_and_time_are_never_null()
    {
        // The_current_date_and_time_functions_are_never_null.
        var doc = Procedure("SELECT SYSDATETIME() AS a, GETDATE() AS b, GETUTCDATE() AS c, SYSUTCDATETIME() AS d, SYSDATETIMEOFFSET() AS e, CURRENT_TIMESTAMP AS f;");
        Assert.Equal("datetime2(7)", Typed(doc, "a"));
        Assert.Equal("datetime", Typed(doc, "b"));
        Assert.Equal("datetime", Typed(doc, "c"));
        Assert.Equal("datetime2(7)", Typed(doc, "d"));
        Assert.Equal("datetimeoffset(7)", Typed(doc, "e"));
        Assert.Equal("datetime", Typed(doc, "f"));
    }
}
