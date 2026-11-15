namespace Haruspex.Tests.Inference;

public partial class FunctionTests
{
    [Fact]
    public void Ascii_and_unicode_are_int_and_out_of_range_unless_given_a_literal_with_a_character()
    {
        // Ascii_and_unicode_are_int_and_null_for_an_empty_string.
        var doc = Procedure("SELECT ASCII(s) AS a, UNICODE(ns) AS b, ASCII('x') AS c, ASCII('') AS d, ASCII(n) AS e FROM dbo.t;");
        Assert.Equal("int out-of-range", Typed(doc, "a"));
        Assert.Equal("int nullable-column out-of-range", Typed(doc, "b"));
        Assert.Equal("int", Typed(doc, "c"));
        Assert.Equal("int out-of-range", Typed(doc, "d"));
        Unknown(doc, "e");
    }

    [Fact]
    public void Ascii_is_from_0_to_255_by_the_engine_and_unicode_has_no_range()
    {
        // Ascii_is_a_byte_from_0_to_255_whatever_the_character_or_collation. UNICODE's bound depends on the collation, so it
        // claims none beyond int's.
        var doc = Procedure("SELECT ASCII(s) AS a, ASCII(N'x') AS c, UNICODE(ns) AS u FROM dbo.t;");
        Assert.Equal(("0", "255", "ASCII"), Range(doc, "a"));
        Assert.Equal(("0", "255", "ASCII"), Range(doc, "c"));
        Assert.Equal(("-2147483648", "2147483647", "int"), Range(doc, "u"));
    }

    [Fact]
    public void Char_is_char_1_out_of_range_but_for_a_tinyint_or_a_literal_in_range_and_overflows_from_a_bigint()
    {
        // Char_is_char_1_and_null_outside_0_to_255_and_overflows_past_int.
        var doc = Procedure("SELECT CHAR(ti) AS a, CHAR(n) AS b, CHAR(65) AS c, CHAR(300) AS d, CHAR(bi) AS e, CHAR(s) AS f FROM dbo.t;");
        Assert.Equal("char(1)", Typed(doc, "a"));
        Assert.Equal("char(1) out-of-range", Typed(doc, "b"));
        Assert.Equal("char(1)", Typed(doc, "c"));
        Assert.Equal("char(1) out-of-range", Typed(doc, "d"));
        Assert.Equal("char(1) nullable-column out-of-range arithmetic-overflow", Typed(doc, "e"));
        Unknown(doc, "f");
    }

    [Fact]
    public void Charindex_and_patindex_are_int_or_bigint_over_max_and_null_only_as_their_arguments_are()
    {
        // Charindex_and_patindex_are_int_or_bigint_over_max_and_null_only_for_a_null_argument.
        var doc = Procedure("SELECT CHARINDEX('a', s) AS a, CHARINDEX(s, nm) AS b, CHARINDEX('a', s, v) AS c, PATINDEX('%a%', c) AS d, PATINDEX(s, vm) AS e, CHARINDEX('a', n) AS f FROM dbo.t;");
        Assert.Equal("int", Typed(doc, "a"));
        Assert.Equal("bigint nullable-column", Typed(doc, "b"));
        Assert.Equal("int nullable-column", Typed(doc, "c"));
        Assert.Equal("int", Typed(doc, "d"));
        Assert.Equal("bigint nullable-column", Typed(doc, "e"));
        Unknown(doc, "f");
    }

    [Fact]
    public void Replicate_and_space_are_as_long_as_a_literal_count_allows_and_out_of_range_for_any_other()
    {
        // Replicate_and_space_are_as_long_as_a_literal_count_allows_up_to_8000_bytes_and_null_for_a_negative_one.
        var doc = Procedure("""
            SELECT REPLICATE(c, 3) AS a, REPLICATE(s, 1000) AS b, REPLICATE(ns, n) AS c, REPLICATE(vm, 2) AS d, REPLICATE(s, 0) AS e,
                   SPACE(5) AS f, SPACE(n) AS g, SPACE(0) AS h, SPACE(9000) AS i, SPACE(bi) AS j
            FROM dbo.t;
            """);
        Assert.Equal("varchar(15)", Typed(doc, "a"));
        Assert.Equal("varchar(8000)", Typed(doc, "b"));
        Assert.Equal("nvarchar(4000) nullable-column out-of-range", Typed(doc, "c"));
        Assert.Equal("varchar(max) nullable-column", Typed(doc, "d"));
        Unknown(doc, "e");
        Assert.Equal("varchar(5)", Typed(doc, "f"));
        Assert.Equal("varchar(8000) out-of-range", Typed(doc, "g"));
        Assert.Equal("varchar(1)", Typed(doc, "h"));
        Assert.Equal("varchar(8000)", Typed(doc, "i"));
        Unknown(doc, "j");
    }

    [Fact]
    public void Reverse_keeps_the_length_and_makes_a_fixed_length_string_varying()
    {
        // Reverse_keeps_the_strings_length_and_makes_it_varying.
        var doc = Procedure("SELECT REVERSE(c) AS a, REVERSE(nm) AS b, REVERSE(n) AS c FROM dbo.t;");
        Assert.Equal("varchar(5)", Typed(doc, "a"));
        Assert.Equal("nvarchar(max) nullable-column", Typed(doc, "b"));
        Unknown(doc, "c");
    }

    [Fact]
    public void Stuff_is_as_long_as_both_strings_less_a_literal_length_and_always_out_of_range()
    {
        // Stuff_is_as_long_as_its_strings_less_a_literal_length_and_null_for_a_start_or_length_it_cannot_use. A NULL replacement
        // only deletes, so its reasons don't count.
        var doc = Procedure("""
            SELECT STUFF(c, 1, 1, 'x') AS a, STUFF(s, 1, 3, s) AS b, STUFF(nc, 1, 1, s) AS c, STUFF(s, n, n, c) AS d,
                   STUFF(vm, 1, 1, 'x') AS e, STUFF(s, 1, 1, ns) AS f, STUFF(c, 9, 1, 'x') AS g, STUFF(n, 1, 1, 'x') AS h, STUFF(c, 2, 9, N'xyz') AS i
            FROM dbo.t;
            """);
        Assert.Equal("varchar(5) out-of-range", Typed(doc, "a"));
        Assert.Equal("varchar(17) out-of-range", Typed(doc, "b"));
        Assert.Equal("nvarchar(13) nullable-column out-of-range", Typed(doc, "c"));
        Assert.Equal("varchar(8000) out-of-range", Typed(doc, "d"));
        Assert.Equal("varchar(max) nullable-column out-of-range", Typed(doc, "e"));
        Assert.Equal("nvarchar(29) out-of-range", Typed(doc, "f"));
        Unknown(doc, "g");
        Unknown(doc, "h");

        // At most what is left from the start is deleted: 'abcde' from 2 keeps 'a'.
        Assert.Equal("nvarchar(4) out-of-range", Typed(doc, "i"));
    }

    [Fact]
    public void Quotename_is_nvarchar_258_and_out_of_range_unless_its_value_fits_128_characters_and_its_quote_is_one_it_reads()
    {
        // Quotename_is_nvarchar_258_and_null_past_128_characters_or_for_a_quote_it_cannot_use. A declared length counts
        // characters at most, in bytes or not.
        var doc = Procedure("""
            SELECT QUOTENAME(s) AS a, QUOTENAME('t') AS b, QUOTENAME('t', '"') AS c, QUOTENAME(n) AS d, QUOTENAME(ns, '`') AS e,
                   QUOTENAME(vm) AS f, QUOTENAME(s, 'x') AS g, QUOTENAME(s, c) AS h, QUOTENAME(c, '[x') AS i
            FROM dbo.t;
            """);
        Assert.Equal("nvarchar(258)", Typed(doc, "a"));
        Assert.Equal("nvarchar(258)", Typed(doc, "b"));
        Assert.Equal("nvarchar(258)", Typed(doc, "c"));
        Unknown(doc, "d");
        Assert.Equal("nvarchar(258) nullable-column", Typed(doc, "e"));
        Assert.Equal("nvarchar(258) nullable-column out-of-range", Typed(doc, "f"));
        Assert.Equal("nvarchar(258) out-of-range", Typed(doc, "g"));
        Assert.Equal("nvarchar(258) out-of-range", Typed(doc, "h"));
        Assert.Equal("nvarchar(258)", Typed(doc, "i"));
    }

    [Fact]
    public void A_start_or_length_wider_than_int_may_overflow_in_charindex_substring_left_and_right()
    {
        // A_start_or_length_past_int_overflows_in_charindex_substring_left_and_right_and_one_not_a_number_is_refused.
        var doc = Procedure("""
            SELECT CHARINDEX('a', s, bi) AS a, CHARINDEX('a', s, ti) AS b, SUBSTRING(s, bi, 2) AS c, SUBSTRING(s, 1, d) AS d2,
                   LEFT(s, bi) AS e, RIGHT(s, n) AS f, LEFT(s, 3) AS g
            FROM dbo.t;
            """);
        Assert.Equal("int nullable-column arithmetic-overflow", Typed(doc, "a"));
        Assert.Equal("int", Typed(doc, "b"));
        Assert.Equal("varchar(2) nullable-column arithmetic-overflow", Typed(doc, "c"));
        Assert.Equal("varchar(10) arithmetic-overflow", Typed(doc, "d2"));
        Assert.Equal("varchar(10) nullable-column arithmetic-overflow", Typed(doc, "e"));
        Assert.Equal("varchar(10)", Typed(doc, "f"));
        Assert.Equal("varchar(3)", Typed(doc, "g"));

        // SUBSTRING refuses one that is neither an integer nor a decimal; LEFT and RIGHT convert it to int, which may overflow,
        // and refuse a date.
        var refused = Procedure("SELECT SUBSTRING(s, f, 1) AS a, LEFT(s, m) AS b, RIGHT(s, s) AS c, LEFT(s, da) AS d FROM dbo.t;");
        Unknown(refused, "a");
        Assert.Equal("varchar(10) arithmetic-overflow", Typed(refused, "b"));
        Assert.Equal("varchar(10) arithmetic-overflow", Typed(refused, "c"));
        Unknown(refused, "d");
    }

    [Fact]
    public void Str_is_varchar_of_its_literal_length_or_8000_and_out_of_range_for_a_length_or_places_not_a_literal()
    {
        // Str_is_varchar_of_its_literal_length_10_by_default_and_null_for_a_length_it_cannot_use.
        var doc = Procedure("SELECT STR(f) AS a, STR(d, 30) AS b, STR(f, n) AS c, STR(f, 5, n) AS d, STR(m, 0) AS e, STR(f, -1) AS f2, STR(s) AS g FROM dbo.t;");
        Assert.Equal("varchar(10)", Typed(doc, "a"));
        Assert.Equal("varchar(30)", Typed(doc, "b"));
        Assert.Equal("varchar(8000) out-of-range", Typed(doc, "c"));
        Assert.Equal("varchar(5) out-of-range", Typed(doc, "d"));
        Assert.Equal("varchar(1)", Typed(doc, "e"));
        Unknown(doc, "f2");
        Unknown(doc, "g");
    }

    [Fact]
    public void Translate_soundex_and_difference_are_null_only_as_their_arguments_are()
    {
        // Translate_is_8000_bytes_unless_its_string_is_max_and_soundex_and_difference_are_fixed.
        var doc = Procedure("SELECT TRANSLATE(c, 'a', 'b') AS a, TRANSLATE(s, ns, ns) AS b, TRANSLATE(vm, 'a', 'b') AS c, SOUNDEX(nm) AS d, DIFFERENCE(s, c) AS e, SOUNDEX(n) AS f FROM dbo.t;");
        Assert.Equal("varchar(8000)", Typed(doc, "a"));
        Assert.Equal("nvarchar(4000) nullable-column", Typed(doc, "b"));
        Assert.Equal("varchar(max) nullable-column", Typed(doc, "c"));
        Assert.Equal("varchar(5) nullable-column", Typed(doc, "d"));
        Assert.Equal("int", Typed(doc, "e"));
        Unknown(doc, "f");
    }
}
