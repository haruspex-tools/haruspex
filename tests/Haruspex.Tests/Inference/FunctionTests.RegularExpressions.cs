namespace Haruspex.Tests.Inference;

public partial class FunctionTests
{
    [Fact]
    public void Regexp_replace_is_8000_bytes_or_max_of_its_strings_kind_and_a_fixed_length_string_is_not_modelled()
    {
        // Regexp_replace_is_8000_bytes_or_max_of_its_strings_kind_and_cuts_a_longer_result_short. Over a fixed-length string the
        // server describes char(8000), a length its value doesn't have until it is stored.
        var doc = Procedure("""
            SELECT REGEXP_REPLACE(s, 'x', 'y') AS a, REGEXP_REPLACE(ns, 'x') AS b, REGEXP_REPLACE(vm, 'x', s, n, 0, 'i') AS c,
                   REGEXP_REPLACE(c, 'a') AS d, REGEXP_REPLACE(s, 'x', 'y', bi) AS e, REGEXP_REPLACE(n, 'x') AS f, REGEXP_REPLACE(s, N'x', N'y') AS g,
                   REGEXP_REPLACE(s, 'x', 'y', 1, 0, n) AS h
            FROM dbo.t;
            """);
        Assert.Equal("varchar(8000)", Typed(doc, "a"));
        Assert.Equal("nvarchar(4000) nullable-column", Typed(doc, "b"));
        Assert.Equal("varchar(max) nullable-column", Typed(doc, "c"));
        Unknown(doc, "d");
        Assert.Equal("varchar(8000) nullable-column arithmetic-overflow", Typed(doc, "e"));
        Unknown(doc, "f");
        Assert.Equal("varchar(8000)", Typed(doc, "g"));
        Unknown(doc, "h");

        // A max replacement makes it max, of the string's kind.
        var max = Procedure("SELECT REGEXP_REPLACE(s, 'x', nm) AS a, REGEXP_REPLACE(ns, 'x', vm, 1, 0, 'i') AS b FROM dbo.t;");
        Assert.Equal("varchar(max) nullable-column", Typed(max, "a"));
        Assert.Equal("nvarchar(max) nullable-column nullable-column", Typed(max, "b"));
    }

    [Fact]
    public void A_regular_expression_call_the_server_refuses_when_it_is_created_is_not_modelled()
    {
        // The_regular_expression_functions_but_regexp_like_are_there_at_every_compatibility_level_and_refuse_what_they_cannot_read.
        var doc = Procedure("""
            SELECT REGEXP_COUNT(s, vm) AS a, REGEXP_COUNT(s, 'x', 1, N'i') AS b, REGEXP_COUNT(s, 'x', ti) AS c, REGEXP_SUBSTR(s, 'x', 1, d) AS d,
                   REGEXP_SUBSTR(s, 'x', 0) AS e, REGEXP_INSTR(s, 'x', 1, 0) AS f, REGEXP_REPLACE(s, 'x', 'y', 1, -1) AS g,
                   REGEXP_INSTR(s, 'x', 1, 1, 2) AS h, REGEXP_COUNT(s, 'x', 1, 'I') AS i, REGEXP_SUBSTR(s, '(x)', 1, 1, '', -1) AS j,
                   REGEXP_COUNT(c, CAST('x' AS nchar(1)), CAST(1 AS smallint), CAST('i' AS char(1))) AS k, REGEXP_INSTR(s, 'x', 1, 1, 0, 'ic', 0) AS l,
                   REGEXP_REPLACE(s, 'x', 'y', 1, 0, 'mi') AS m
            FROM dbo.t;
            """);
        foreach (var name in new[] { "a", "b", "c", "d", "e", "f", "g", "h", "i", "j" })
            Unknown(doc, name);
        Assert.Equal("int", Typed(doc, "k"));
        Assert.Equal("int", Typed(doc, "l"));
        Assert.Equal("varchar(8000)", Typed(doc, "m"));
    }

    [Fact]
    public void Regexp_substr_is_its_strings_length_varying_and_always_out_of_range()
    {
        // Regexp_substr_is_its_strings_length_varying_and_null_when_nothing_matches.
        var doc = Procedure("""
            SELECT REGEXP_SUBSTR(s, 'x') AS a, REGEXP_SUBSTR(c, 'a', v) AS b, REGEXP_SUBSTR(nc, N'a') AS c, REGEXP_SUBSTR(nm, 'a', 1, 1, 'i', 0) AS d,
                   REGEXP_SUBSTR(s, 'x', 1, bi) AS e, REGEXP_SUBSTR(da, 'x') AS f, REGEXP_SUBSTR('abc', 'b') AS g
            FROM dbo.t;
            """);
        Assert.Equal("varchar(10) out-of-range", Typed(doc, "a"));
        Assert.Equal("varchar(5) nullable-column out-of-range", Typed(doc, "b"));
        Assert.Equal("nvarchar(4) nullable-column out-of-range", Typed(doc, "c"));
        Assert.Equal("nvarchar(max) nullable-column out-of-range", Typed(doc, "d"));
        Assert.Equal("varchar(10) nullable-column arithmetic-overflow out-of-range", Typed(doc, "e"));
        Unknown(doc, "f");
        Assert.Equal("varchar(3) out-of-range", Typed(doc, "g"));
    }

    [Fact]
    public void Regexp_instr_and_regexp_count_are_int_from_0_and_null_only_as_their_arguments_are()
    {
        // Regexp_instr_and_regexp_count_are_int_from_0_and_null_only_for_a_null_argument.
        var doc = Procedure("""
            SELECT REGEXP_INSTR(s, 'x') AS a, REGEXP_INSTR(vm, 'x', 1, 1, 1, 'i', 0) AS b, REGEXP_COUNT(s, ns) AS c, REGEXP_COUNT(s, 'x', bi) AS d,
                   REGEXP_COUNT(c, 'a', 2, 'c') AS e, REGEXP_COUNT(n, 'x') AS f
            FROM dbo.t;
            """);
        Assert.Equal("int", Typed(doc, "a"));
        Assert.Equal(("0", "2147483647", "REGEXP_INSTR"), Range(doc, "a"));
        Assert.Equal("int nullable-column", Typed(doc, "b"));
        Assert.Equal("int nullable-column", Typed(doc, "c"));
        Assert.Equal(("0", "2147483647", "REGEXP_COUNT"), Range(doc, "c"));
        Assert.Equal("int nullable-column arithmetic-overflow", Typed(doc, "d"));
        Assert.Equal("int", Typed(doc, "e"));
        Unknown(doc, "f");
    }

    [Fact]
    public void Regexp_like_as_a_condition_leaves_nothing_unknown()
    {
        // A predicate: it filters rows and chooses branches, and proves nothing about a column.
        var doc = Procedure("SELECT id, CASE WHEN REGEXP_LIKE(s, '^a') THEN 1 ELSE 0 END AS k, ns FROM dbo.t WHERE REGEXP_LIKE(ns, 'b', 'i');");
        Assert.Empty(doc.Unknowns);
        Assert.Equal("int", Typed(doc, "k"));
        Assert.Equal("nvarchar(20) nullable-column", Typed(doc, "ns"));
    }
}
