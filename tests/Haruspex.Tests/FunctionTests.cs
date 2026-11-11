using Haruspex.Ir;

namespace Haruspex.Tests;

/// <summary>
/// The built-in functions haruspex types: date and string functions, <c>STRING_AGG</c>, <c>JSON_VALUE</c>, window functions,
/// arithmetic and precedence over <c>float</c>, <c>real</c>, <c>money</c> and the date and time types, the bare <c>NULL</c>,
/// and <c>GENERATE_SERIES</c>. Each type and each reason to be null rests on an observation in the differential tests'
/// <c>FunctionObservationTests</c>, named where it matters.
/// </summary>
public class FunctionTests
{
    private const string Tables = """
        CREATE TABLE dbo.t (
            id int NOT NULL PRIMARY KEY, g int NOT NULL, n int NOT NULL, v int NULL, ti tinyint NOT NULL, bi bigint NULL,
            s varchar(10) NOT NULL, ns nvarchar(20) NULL, c char(5) NOT NULL, nc nchar(4) NULL, vm varchar(max) NULL, nm nvarchar(max) NULL,
            d decimal(9,2) NOT NULL, nd numeric(5,1) NULL, f float NOT NULL, r real NULL, m money NOT NULL, sm smallmoney NULL,
            da date NOT NULL, dt datetime NOT NULL, sdt smalldatetime NULL, d2 datetime2(3) NOT NULL, d21 datetime2(1) NULL,
            tm time(2) NULL, dto datetimeoffset(4) NULL, doc nvarchar(max) NULL,
            status varchar(10) NOT NULL CONSTRAINT ck_status CHECK (status IN ('open', 'shut')));
        GO
        """;

    private static IrDocument Procedure(string body) =>
        InferProcedure.Infer("dbo.p", ("tables.sql", Tables), ("p.sql", $"CREATE PROCEDURE dbo.p AS\n{body}"));

    // Stricter than IrNavigation.Fields: asserts one result, an array of rows.
    private static IReadOnlyList<IrField> Fields(IrDocument doc)
    {
        var results = doc.Objects[0].Results ?? throw new InvalidOperationException("The results are unknown: " + string.Join("; ", doc.Unknowns.Select(u => u.Detail)));
        var row = (IrRef)((IrArray)Assert.Single(results).Type).Items;
        return doc.Types[row.Name].Fields;
    }

    private static IrField Field(IrDocument doc, string name) => Assert.Single(Fields(doc), f => f.Name == name);

    /// <summary>The field's SQL type and its reasons to be null, <c>int nullable-column</c>, or "unknown: reason".</summary>
    private static string Typed(IrDocument doc, string name) => Field(doc, name).Type switch
    {
        IrScalar scalar => string.Join(" ", [scalar.Sql ?? "(none)", .. Field(doc, name).NullableBecause.Select(r => r.Rule)]),
        _ => "unknown: " + string.Join("; ", doc.Unknowns.Where(u => u.Path.EndsWith("." + name, StringComparison.Ordinal)).Select(u => u.Reason)),
    };

    private static void Unknown(IrDocument doc, string name) => Assert.StartsWith("unknown", Typed(doc, name), StringComparison.Ordinal);

    // ---- date functions -----------------------------------------------------------------------------------------------

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

    // ---- string functions -----------------------------------------------------------------------------------------------

    [Fact]
    public void Trimming_and_case_keep_the_length_and_make_fixed_length_varying()
    {
        // Trimming_and_case_keep_the_strings_length_and_make_a_fixed_length_string_varying.
        var doc = Procedure("""
            SELECT LTRIM(s) AS a1, RTRIM(c) AS a2, TRIM(nc) AS a3, LTRIM(vm) AS a4, UPPER(c) AS a5, LOWER(nc) AS a6, UPPER(nm) AS a7,
                   TRIM('x' FROM s) AS a8, TRIM(N'x' FROM s) AS a9, TRIM(s FROM ns) AS a10, LTRIM(s, N'x') AS a11, UPPER('abc') AS a12, LOWER(status) AS a13
            FROM dbo.t;
            """);
        Assert.Equal("varchar(10)", Typed(doc, "a1"));
        Assert.Equal("varchar(5)", Typed(doc, "a2"));
        Assert.Equal("nvarchar(4) nullable-column", Typed(doc, "a3"));
        Assert.Equal("varchar(max) nullable-column", Typed(doc, "a4"));
        Assert.Equal("varchar(5)", Typed(doc, "a5"));
        Assert.Equal("nvarchar(4) nullable-column", Typed(doc, "a6"));
        Assert.Equal("nvarchar(max) nullable-column", Typed(doc, "a7"));
        Assert.Equal("varchar(10)", Typed(doc, "a8"));
        Assert.Equal("nvarchar(10)", Typed(doc, "a9"));
        Assert.Equal("nvarchar(20) nullable-column", Typed(doc, "a10"));
        Assert.Equal("nvarchar(10)", Typed(doc, "a11"));
        Assert.Equal("varchar(3)", Typed(doc, "a12"));

        // A CHECK set does not survive: LOWER changes the values.
        Assert.Equal("varchar(10)", Typed(doc, "a13"));
        Assert.Null(((IrScalar)Field(doc, "a13").Type).Facets?.Values);
    }

    [Fact]
    public void Left_right_and_substring_are_as_long_as_a_literal_length_allows_and_null_when_an_argument_is()
    {
        // Left_right_and_substring_are_as_long_as_a_literal_length_allows.
        var doc = Procedure("""
            SELECT LEFT(s, 3) AS b1, LEFT(c, 2) AS b2, RIGHT(nc, n) AS b3, SUBSTRING(s, 9, 3) AS b4, SUBSTRING(s, 1, 20) AS b5, SUBSTRING(nm, 1, 2) AS b6,
                   LEFT(vm, 5) AS b7, LEFT(s, 0) AS b8, LEFT(s, v) AS b9, SUBSTRING(s, v, 2) AS b10, LEFT(s, (4)) AS b11,
                   LEFT(s, 1 + 1) AS b12, LEFT(s, 2.7) AS b13
            FROM dbo.t;
            """);
        Assert.Equal("varchar(3)", Typed(doc, "b1"));
        Assert.Equal("varchar(2)", Typed(doc, "b2"));
        Assert.Equal("nvarchar(4) nullable-column", Typed(doc, "b3"));
        Assert.Equal("varchar(3)", Typed(doc, "b4"));
        Assert.Equal("varchar(10)", Typed(doc, "b5"));
        Assert.Equal("nvarchar(max) nullable-column", Typed(doc, "b6"));
        Assert.Equal("varchar(max) nullable-column", Typed(doc, "b7"));
        Assert.Equal("varchar(1)", Typed(doc, "b8"));
        Assert.Equal("varchar(10) nullable-column", Typed(doc, "b9"));
        Assert.Equal("varchar(2) nullable-column", Typed(doc, "b10"));
        Assert.Equal("varchar(4)", Typed(doc, "b11"));

        // A constant length is folded by the server (1 + 1 gives varchar(2)) where a decimal literal is not: not modelled.
        Unknown(doc, "b12");
        Unknown(doc, "b13");
    }

    [Fact]
    public void Len_datalength_replace_and_string_escape()
    {
        // Len_and_datalength_are_int_or_bigint_over_max_and_len_ignores_trailing_spaces;
        // Replace_widens_to_8000_bytes_unless_its_string_is_max; String_escape_is_nvarchar_max_…
        var doc = Procedure("""
            SELECT LEN(s) AS c1, LEN(vm) AS c2, LEN(nm) AS c3, DATALENGTH(s) AS c4, DATALENGTH(nm) AS c5, DATALENGTH(n) AS c6, LEN(c) AS c7, DATALENGTH(d2) AS c8,
                   REPLACE(s, 'a', 'b') AS d1, REPLACE(ns, 'a', 'b') AS d2, REPLACE(vm, 'a', 'b') AS d3, REPLACE(s, N'a', 'b') AS d4, REPLACE(s, nm, 'b') AS d5,
                   REPLACE(s, 'a', vm) AS d6, REPLACE(nm, 'a', 'b') AS d7,
                   STRING_ESCAPE(s, 'json') AS e1, STRING_ESCAPE(nm, 'json') AS e2
            FROM dbo.t;
            """);
        Assert.Equal("int", Typed(doc, "c1"));
        Assert.Equal("bigint nullable-column", Typed(doc, "c2"));
        Assert.Equal("bigint nullable-column", Typed(doc, "c3"));
        Assert.Equal("int", Typed(doc, "c4"));
        Assert.Equal("bigint nullable-column", Typed(doc, "c5"));
        Assert.Equal("int", Typed(doc, "c6"));
        Assert.Equal("int", Typed(doc, "c7"));
        Assert.Equal("int", Typed(doc, "c8"));
        Assert.Equal("varchar(8000)", Typed(doc, "d1"));
        Assert.Equal("nvarchar(4000) nullable-column", Typed(doc, "d2"));
        Assert.Equal("varchar(max) nullable-column", Typed(doc, "d3"));
        Assert.Equal("nvarchar(4000)", Typed(doc, "d4"));
        Assert.Equal("nvarchar(4000) nullable-column", Typed(doc, "d5"));
        Assert.Equal("varchar(8000) nullable-column", Typed(doc, "d6"));
        Assert.Equal("nvarchar(max) nullable-column", Typed(doc, "d7"));
        Assert.Equal("nvarchar(max)", Typed(doc, "e1"));
        Assert.Equal("nvarchar(max) nullable-column", Typed(doc, "e2"));
    }

    [Fact]
    public void Formatmessage_is_null_only_for_a_null_format_and_json_value_always_may_be()
    {
        // String_escape_is_nvarchar_max_and_formatmessage_nvarchar_4000_writing_a_null_argument_as_null_in_brackets;
        // Json_value_is_nvarchar_4000_and_null_for_a_json_null_a_missing_path_an_object_or_a_long_string.
        var doc = Procedure("""
            SELECT FORMATMESSAGE('%s-%d', ns, v) AS f1, FORMATMESSAGE(ns, n) AS f2, FORMATMESSAGE(50001, n) AS f3,
                   JSON_VALUE(doc, '$.a') AS g1, JSON_VALUE(N'{"a":1}', 'strict $.a') AS g2
            FROM dbo.t;
            """);
        Assert.Equal("nvarchar(4000)", Typed(doc, "f1"));
        Assert.Equal("nvarchar(4000) nullable-column", Typed(doc, "f2"));

        // A message number names a message that may not exist: not modelled.
        Unknown(doc, "f3");
        Assert.Equal("nvarchar(4000) json-value", Typed(doc, "g1"));

        // Over a literal JSON document the path's value is known: a number, always there under strict.
        Assert.Equal("nvarchar(4000)", Typed(doc, "g2"));
    }

    [Fact]
    public void A_string_function_of_a_value_that_is_not_a_string_is_not_typed()
    {
        var doc = Procedure("SELECT LTRIM(n) AS a, LEFT(n, 2) AS b, LEN(d) AS c, REPLACE(n, 1, 2) AS e, UPPER(da) AS f FROM dbo.t;");
        Unknown(doc, "a");
        Unknown(doc, "b");
        Unknown(doc, "c");
        Unknown(doc, "e");
        Unknown(doc, "f");
    }

    [Fact]
    public void A_built_in_read_twice_by_coalesce_gives_the_same_value()
    {
        // The functions modelled here read only their arguments, as CONCAT and ISNULL do, so COALESCE reading one again gets
        // the same value: no read-twice. A window function is still counted as read twice (not observed otherwise).
        var doc = Procedure("""
            SELECT COALESCE(UPPER(ns), N'') AS a, COALESCE(JSON_VALUE(doc, '$.a'), N'') AS b, COALESCE(DATEADD(day, v, da), da) AS c,
                   COALESCE(LAG(v) OVER (ORDER BY id), 0) AS d
            FROM dbo.t;
            """);
        Assert.Equal("nvarchar(20)", Typed(doc, "a"));
        Assert.Equal("nvarchar(4000)", Typed(doc, "b"));
        Assert.Equal("date", Typed(doc, "c"));
        Assert.Equal("int read-twice", Typed(doc, "d"));
    }

    // ---- STRING_AGG -----------------------------------------------------------------------------------------------------

    [Fact]
    public void String_agg_is_8000_bytes_unless_max_and_null_over_no_rows_or_only_nulls()
    {
        // String_agg_is_8000_bytes_unless_max_and_null_over_no_rows_or_only_nulls;
        // String_agg_past_its_limit_is_an_error_even_under_the_callers_ansi_warnings_and_arithabort_off.
        var doc = Procedure("""
            SELECT STRING_AGG(s, ',') AS a1, STRING_AGG(ns, ',') AS a2, STRING_AGG(vm, ',') WITHIN GROUP (ORDER BY id) AS a3, STRING_AGG(c, '-') AS a4,
                   STRING_AGG(n, ',') AS a5, STRING_AGG(nm, N',') AS a6, STRING_AGG(da, ',') AS a7, ISNULL(STRING_AGG(s, ','), '') AS a8
            FROM dbo.t;
            """);
        Assert.Equal("varchar(8000) empty-aggregate", Typed(doc, "a1"));
        Assert.Equal("nvarchar(4000) nullable-column empty-aggregate", Typed(doc, "a2"));
        Assert.Equal("varchar(max) nullable-column empty-aggregate", Typed(doc, "a3"));
        Assert.Equal("varchar(8000) empty-aggregate", Typed(doc, "a4"));
        Assert.Equal("nvarchar(4000) empty-aggregate", Typed(doc, "a5"));
        Assert.Equal("nvarchar(max) nullable-column empty-aggregate", Typed(doc, "a6"));
        Assert.Equal("nvarchar(4000) empty-aggregate", Typed(doc, "a7"));
        Assert.Equal("varchar(8000)", Typed(doc, "a8"));

        var grouped = Procedure("SELECT g, STRING_AGG(s, ',') AS a, STRING_AGG(ns, ',') AS b FROM dbo.t GROUP BY g;");
        Assert.Equal("varchar(8000)", Typed(grouped, "a"));
        Assert.Equal("nvarchar(4000) nullable-column", Typed(grouped, "b"));
    }

    // ---- window functions ---------------------------------------------------------------------------------------------

    [Fact]
    public void Ranking_is_bigint_and_never_null()
    {
        // Ranking_is_never_null_and_percentiles_are_null_only_over_all_null_input.
        var doc = Procedure("SELECT ROW_NUMBER() OVER (ORDER BY id) AS a, RANK() OVER (PARTITION BY g ORDER BY v) AS b, DENSE_RANK() OVER (ORDER BY v) AS c, NTILE(4) OVER (ORDER BY id) AS d FROM dbo.t;");
        Assert.Equal("bigint", Typed(doc, "a"));
        Assert.Equal("bigint", Typed(doc, "b"));
        Assert.Equal("bigint", Typed(doc, "c"));
        Assert.Equal("bigint", Typed(doc, "d"));
    }

    [Fact]
    public void An_aggregate_over_a_window_is_null_as_its_values_are_or_when_its_frame_may_be_empty()
    {
        // A_frame_that_may_leave_out_the_current_row_can_be_empty_and_give_null_over_a_never_null_column.
        var doc = Procedure("""
            SELECT COUNT(*) OVER () AS a, COUNT_BIG(v) OVER (PARTITION BY g) AS b, SUM(n) OVER (PARTITION BY g) AS c, SUM(v) OVER () AS d,
                   SUM(n) OVER (ORDER BY id ROWS BETWEEN 2 PRECEDING AND 1 PRECEDING) AS e, MAX(s) OVER (ORDER BY id) AS f,
                   MIN(n) OVER (ORDER BY id ROWS BETWEEN 1 FOLLOWING AND UNBOUNDED FOLLOWING) AS g2, SUM(d) OVER (ORDER BY id ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS h,
                   COUNT(n) OVER (ORDER BY id ROWS BETWEEN 1 FOLLOWING AND 2 FOLLOWING) AS i, MAX(status) OVER () AS j, SUM(n) OVER (ORDER BY id ROWS 2 PRECEDING) AS k
            FROM dbo.t;
            """);

        // COUNT is an int, which over more than 2,147,483,647 rows would overflow, as for COUNT without a window.
        Assert.Equal("int count-overflow", Typed(doc, "a"));
        Assert.Equal("bigint", Typed(doc, "b"));
        Assert.Equal("int arithmetic-overflow", Typed(doc, "c"));
        Assert.Equal("int nullable-column arithmetic-overflow", Typed(doc, "d"));
        Assert.Equal("int arithmetic-overflow empty-frame", Typed(doc, "e"));
        Assert.Equal("varchar(10)", Typed(doc, "f"));
        Assert.Equal("int empty-frame", Typed(doc, "g2"));
        Assert.Equal("decimal(38,2)", Typed(doc, "h"));
        Assert.Equal("int count-overflow", Typed(doc, "i"));
        Assert.Equal("int arithmetic-overflow", Typed(doc, "k"));

        // MIN and MAX give one of the column's values, so its CHECK set holds.
        Assert.NotNull(((IrScalar)Field(doc, "j").Type).Facets?.Values);
    }

    [Fact]
    public void Lag_and_lead_are_null_past_the_edge_unless_a_default_never_null_is_given()
    {
        // Lag_and_lead_are_null_past_the_partitions_edge_unless_a_default_is_given_which_is_converted_to_the_columns_type.
        var doc = Procedure("""
            SELECT LAG(n) OVER (ORDER BY id) AS a, LAG(n, 1, 0) OVER (ORDER BY id) AS b, LEAD(s, 1, 'a longer default') OVER (ORDER BY id) AS c,
                   LAG(ti, 1, 1000) OVER (ORDER BY id) AS d, LAG(v, 2, 0) OVER (ORDER BY id) AS e, LAG(n, 1, v) OVER (ORDER BY id) AS f,
                   LAG(n, 1, NULL) OVER (ORDER BY id) AS g2, LAG(n, v) OVER (ORDER BY id) AS h, LAG(n, 0) OVER (ORDER BY id) AS i,
                   LAG(status) OVER (ORDER BY id) AS j, LAG(status, 1, 'none') OVER (ORDER BY id) AS k, LEAD(ti, 1, 1.9) OVER (ORDER BY id) AS l
            FROM dbo.t;
            """);
        Assert.Equal("int window-edge", Typed(doc, "a"));
        Assert.Equal("int", Typed(doc, "b"));
        Assert.Equal("varchar(10)", Typed(doc, "c"));
        Assert.Equal("tinyint arithmetic-overflow", Typed(doc, "d"));
        Assert.Equal("int nullable-column", Typed(doc, "e"));
        Assert.Equal("int nullable-column", Typed(doc, "f"));
        Assert.Equal("int window-edge", Typed(doc, "g2"));
        Assert.Equal("int window-edge nullable-column", Typed(doc, "h"));
        Assert.Equal("int", Typed(doc, "i"));
        Assert.Equal("tinyint", Typed(doc, "l"));

        // Without a default, the column's own values (and its CHECK set); with one, which may be outside it, only the type's.
        Assert.Equal("varchar(10) window-edge", Typed(doc, "j"));
        Assert.NotNull(((IrScalar)Field(doc, "j").Type).Facets?.Values);
        Assert.Null(((IrScalar)Field(doc, "k").Type).Facets?.Values);
    }

    [Fact]
    public void First_and_last_value_and_percentiles()
    {
        // A_frame_that_may_leave_out_the_current_row_can_be_empty_…; Ranking_is_never_null_and_percentiles_are_null_only_over_all_null_input.
        var doc = Procedure("""
            SELECT FIRST_VALUE(n) OVER (ORDER BY id) AS a, LAST_VALUE(s) OVER (ORDER BY id ROWS BETWEEN 1 FOLLOWING AND 2 FOLLOWING) AS b,
                   FIRST_VALUE(v) OVER (PARTITION BY g ORDER BY id) AS c, LAST_VALUE(n) OVER (ORDER BY id ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING) AS d,
                   PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY n) OVER () AS e, PERCENTILE_DISC(0.5) WITHIN GROUP (ORDER BY v) OVER (PARTITION BY g) AS f,
                   PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY d) OVER () AS h, PERCENTILE_DISC(0.25) WITHIN GROUP (ORDER BY s DESC) OVER () AS i,
                   PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY CAST(n AS bit)) OVER () AS j, PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY s) OVER () AS k
            FROM dbo.t;
            """);
        Assert.Equal("int", Typed(doc, "a"));
        Assert.Equal("varchar(10) empty-frame", Typed(doc, "b"));
        Assert.Equal("int nullable-column", Typed(doc, "c"));
        Assert.Equal("int", Typed(doc, "d"));
        Assert.Equal("float", Typed(doc, "e"));
        Assert.Equal("int nullable-column", Typed(doc, "f"));
        Assert.Equal("float", Typed(doc, "h"));
        Assert.Equal("varchar(10)", Typed(doc, "i"));

        // A bit is a number to PERCENTILE_CONT; a string is an error.
        Assert.Equal("float", Typed(doc, "j"));
        Unknown(doc, "k");
    }

    [Fact]
    public void A_window_function_not_modelled_is_unknown()
    {
        // AVG over a window is read (NumericTests).
        var doc = Procedure("SELECT CUME_DIST() OVER (ORDER BY id) AS a, PERCENT_RANK() OVER (ORDER BY id) AS b FROM dbo.t;");
        Unknown(doc, "a");
        Unknown(doc, "b");
    }

    // ---- arithmetic and precedence ------------------------------------------------------------------------------------

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

    // ---- NULL and GENERATE_SERIES ---------------------------------------------------------------------------------------

    [Fact]
    public void A_bare_null_is_an_int_that_is_always_null()
    {
        // A_bare_null_is_an_int.
        var doc = Procedure("SELECT NULL AS n, CAST(NULL AS date) AS d, DATEADD(day, NULL, da) AS e FROM dbo.t;");
        Assert.Equal("int null-literal", Typed(doc, "n"));
        Assert.Equal("date null-literal", Typed(doc, "d"));

        // DATEADD with the NULL literal as its number does not compile (error 8116).
        Unknown(doc, "e");
    }

    [Fact]
    public void Generate_series_gives_value_of_its_arguments_type_never_null()
    {
        // Generate_series_gives_value_of_its_arguments_type_never_null_and_no_rows_for_a_null_argument.
        var doc = Procedure("""
            SELECT g.value AS a FROM GENERATE_SERIES(1, 10, 2) AS g;
            SELECT value AS b FROM dbo.t AS t CROSS APPLY GENERATE_SERIES(CAST(1 AS tinyint), t.ti) AS g;
            SELECT value AS c FROM dbo.t AS t CROSS APPLY GENERATE_SERIES(t.nd, 3.25) AS g;
            SELECT value AS d FROM dbo.t AS t CROSS APPLY GENERATE_SERIES(t.ti, 10) AS g;
            SELECT t.id, s.value AS e FROM dbo.t AS t CROSS APPLY GENERATE_SERIES(1, t.n) AS s;
            SELECT value AS f FROM dbo.t AS t CROSS APPLY GENERATE_SERIES(t.d, 100.5) AS g;
            """);
        string Of(int index, string name)
        {
            var result = doc.Objects[0].Results![index];
            var row = (IrRef)((IrArray)result.Type).Items;
            var field = Assert.Single(doc.Types[row.Name].Fields, f => f.Name == name);
            return field.Type is IrScalar scalar ? string.Join(" ", [scalar.Sql!, .. field.NullableBecause.Select(r => r.Rule)]) : "unknown";
        }

        Assert.Equal("int", Of(0, "a"));
        Assert.Equal("tinyint", Of(1, "b"));
        Assert.Equal("numeric(6,2)", Of(2, "c"));

        // Arguments of different types are an error on the server.
        Assert.Equal("unknown", Of(3, "d"));
        Assert.Equal("int", Of(4, "e"));

        // decimal beside a numeric literal is an error too (error 5373): the names must be the same.
        Assert.Equal("unknown", Of(5, "f"));
    }
}
