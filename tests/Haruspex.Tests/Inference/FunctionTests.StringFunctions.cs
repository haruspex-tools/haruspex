using Haruspex.Ir;

namespace Haruspex.Tests.Inference;

public partial class FunctionTests
{
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
        Assert.Equal("nvarchar(4000) json-null-or-absent", Typed(doc, "g1"));

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
}
