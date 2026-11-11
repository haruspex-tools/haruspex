using Haruspex.Ir;

namespace Haruspex.Tests;

/// <summary>
/// A column whose values have different types: a set operation's, <c>COALESCE</c>'s and a <c>CASE</c>'s, all typed by one rule
/// (<c>ScalarType.Common</c>). Each rule rests on an observation in the differential tests' <c>SetTypeObservationTests</c>,
/// named where it matters; <c>SetTypeGridTests</c> compares the types with the server's over every pair of a grid.
/// </summary>
public class SetTypeTests
{
    private const string Tables = """
        CREATE TABLE dbo.sale (
            id int NOT NULL PRIMARY KEY,
            qty int NOT NULL CONSTRAINT ck_qty CHECK (qty BETWEEN 1 AND 500),
            tiny tinyint NOT NULL, big bigint NOT NULL,
            price decimal(9,2) NOT NULL CONSTRAINT ck_price CHECK (price >= 0),
            amount decimal(38,10) NOT NULL, whole decimal(30,0) NOT NULL, frac decimal(38,38) NOT NULL,
            tier decimal(38,0) NOT NULL CONSTRAINT ck_tier CHECK (tier IN (1, 2)),
            fee money NOT NULL, small smallmoney NOT NULL, rate float NOT NULL, ratio real NOT NULL, flag bit NOT NULL,
            code varchar(10) NOT NULL,
            kind varchar(10) NOT NULL CONSTRAINT ck_kind CHECK (kind IN ('stall', 'web')),
            nkind nvarchar(20) NOT NULL CONSTRAINT ck_nkind CHECK (nkind IN (N'auction')),
            digits varchar(3) NOT NULL CONSTRAINT ck_digits CHECK (digits IN ('1', '2')),
            fixed char(4) NOT NULL, note varchar(max) NULL,
            sold date NOT NULL CONSTRAINT ck_sold CHECK (sold IN ('2026-05-02', '2026-05-03')), at_time time(3) NOT NULL, stamp datetime NOT NULL, small_stamp smalldatetime NOT NULL,
            moment datetime2(7) NOT NULL, zoned datetimeoffset(3) NOT NULL,
            ref uniqueidentifier NOT NULL, ref_text varchar(36) NOT NULL, doc xml NULL);
        GO
        """;

    private static IrDocument Procedure(string body) =>
        InferProcedure.Infer("dbo.p", ("tables.sql", Tables), ("p.sql", $"CREATE PROCEDURE dbo.p AS\n{body}"));

    // Stricter than IrNavigation.Field: the first result must be an array of rows.
    private static IrField Field(IrDocument doc, string name)
    {
        var results = doc.Objects[0].Results ?? throw new InvalidOperationException("The results are unknown: " + string.Join("; ", doc.Unknowns.Select(u => u.Detail)));
        var row = Assert.IsType<IrRef>(Assert.IsType<IrArray>(results[0].Type).Items);
        return Assert.Single(doc.Types[row.Name].Fields, f => f.Name == name);
    }

    /// <summary>The type and the reasons to be null, as one string: <c>smallmoney arithmetic-overflow</c>.</summary>
    private static string Typed(IrDocument doc, string name = "v")
    {
        var field = Field(doc, name);
        if (field.Type is not IrScalar { Sql: { } sql })
            return "unknown: " + string.Join("; ", doc.Unknowns.Select(u => u.Detail));
        return string.Join(" ", [sql, .. field.NullableBecause.Select(r => r.Rule)]);
    }

    private static string Union(string left, string right) => Typed(Procedure($"SELECT s.{left} AS v FROM dbo.sale AS s UNION ALL SELECT s.{right} FROM dbo.sale AS s;"));

    private static IrFacets? Facets(IrDocument doc, string name = "v") => Assert.IsType<IrScalar>(Field(doc, name).Type).Facets;

    private static string? Values(IrDocument doc, string name = "v") =>
        Facets(doc, name)?.Values is { } set ? string.Join("|", set.Items.Select(i => i.Text)) : null;

    // ---- types ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void Exact_numbers_past_38_digits_keep_their_integer_digits_and_cut_the_scale_without_overflowing()
    {
        // Exact_numbers_past_38_digits_keep_their_integer_digits_and_cut_the_scale;
        // A_cut_scale_rounds_but_never_overflows_since_the_integer_digits_are_kept.
        Assert.Equal("decimal(38,8)", Union("amount", "whole"));
        Assert.Equal("decimal(38,23)", Union("frac", "fee"));
        Assert.Equal("decimal(38,28)", Union("frac", "qty"));
        Assert.Equal("decimal(38,0)", Union("frac", "tier"));
    }

    [Fact]
    public void A_chain_is_typed_left_to_right_so_money_absorbs_an_integer_before_a_decimal()
    {
        // A_chain_is_typed_left_to_right_so_money_absorbs_an_integer_before_a_decimal: the bigint must become
        // decimal(19,4), which it may overflow.
        static string Chain(params string[] columns) =>
            Typed(Procedure(string.Join(" UNION ALL ", columns.Select((c, i) => $"SELECT s.{c}{(i == 0 ? " AS v" : "")} FROM dbo.sale AS s")) + ";"));
        Assert.Equal("decimal(19,4) arithmetic-overflow", Chain("big", "fee", "price"));
        Assert.Equal("decimal(23,4)", Chain("big", "price", "fee"));
        Assert.Equal("decimal(11,4) arithmetic-overflow", Chain("qty", "small", "price"));
        Assert.Equal("decimal(14,4)", Chain("qty", "price", "small"));
        Assert.Equal("decimal(38,23) arithmetic-overflow", Chain("big", "fee", "frac"));
        Assert.Equal("decimal(19,4) arithmetic-overflow", Typed(Procedure("SELECT COALESCE(s.big, s.fee, s.price) AS v FROM dbo.sale AS s;")));
        Assert.Equal("decimal(19,4) arithmetic-overflow", Typed(Procedure("SELECT CHOOSE(s.id, s.big, s.fee, s.price) AS v FROM dbo.sale AS s;")).Replace(" out-of-range", "", StringComparison.Ordinal));

        // Dates keep the most fractional digits of all (A_chain_takes_the_most_fractional_digits_of_every_date_and_time).
        Assert.Equal("datetime2(7)", Chain("at_time", "sold", "moment"));
        Assert.StartsWith("datetime2(7)", Typed(Procedure("SELECT s.zoned AS v FROM dbo.sale AS s UNION ALL SELECT CAST('10:00' AS time(7)) FROM dbo.sale AS s;")).Replace("datetimeoffset(7)", "datetime2(7)", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void A_chain_is_refused_only_when_a_value_cannot_become_its_type()
    {
        // A_chain_is_refused_only_when_a_value_cannot_become_its_type.
        Assert.StartsWith("datetime ", Typed(Procedure("SELECT 1 AS v UNION ALL SELECT s.sold FROM dbo.sale AS s UNION ALL SELECT s.stamp FROM dbo.sale AS s;")), StringComparison.Ordinal);
        Assert.Equal("datetime2(7)", Typed(Procedure("SELECT s.sold AS v FROM dbo.sale AS s UNION ALL SELECT s.at_time FROM dbo.sale AS s UNION ALL SELECT s.moment FROM dbo.sale AS s;")));
        Assert.StartsWith("datetime ", Typed(Procedure("SELECT COALESCE(1, s.sold, s.stamp) AS v FROM dbo.sale AS s;")), StringComparison.Ordinal);
        var refused = Typed(Procedure("SELECT s.qty AS v FROM dbo.sale AS s UNION ALL SELECT s.ref FROM dbo.sale AS s UNION ALL SELECT s.code FROM dbo.sale AS s;"));
        Assert.Contains("int and uniqueidentifier", refused, StringComparison.Ordinal);
    }

    [Fact]
    public void An_int_constant_counts_its_own_digits_and_one_the_server_may_fold_leaves_the_scale_unknown()
    {
        // An_int_constant_counts_its_own_digits_beside_a_decimal.
        static string Beside(string other) => Typed(Procedure($"SELECT s.frac AS v FROM dbo.sale AS s UNION ALL SELECT {other} FROM dbo.sale AS s;"));
        Assert.Equal("decimal(38,37) null-literal", Beside("CAST(NULL AS int)"));
        Assert.Equal("decimal(38,37) null-literal", Beside("TRY_CONVERT(int, NULL)"));
        Assert.Equal("decimal(38,33) null-literal", Beside("CAST(NULL AS smallint)"));
        Assert.Equal("decimal(38,36)", Beside("-(12)"));
        Assert.Equal("decimal(38,28)", Beside("CAST(2 AS int)"));
        Assert.Equal("decimal(38,28)", Beside("(SELECT 1)"));
        Assert.Equal("decimal(38,37)", Typed(Procedure("SELECT s.frac AS v FROM dbo.sale AS s UNION ALL SELECT q.y FROM (SELECT 1 AS y) AS q;")));
        Assert.StartsWith("unknown: ", Beside("IIF(s.id > 0, 1, 2)"), StringComparison.Ordinal);
        Assert.Contains("folds", Beside("CASE WHEN s.id > 0 THEN 1 END"), StringComparison.Ordinal);
        Assert.Equal("decimal(38,37)", Typed(Procedure("SELECT COALESCE(s.frac, CAST(NULL AS int)) AS v FROM dbo.sale AS s;")));

        // Beside a decimal(9,2) the scale is 2 however the server folds it: typed, with the most digits.
        Assert.Equal("decimal(12,2)", Typed(Procedure("SELECT s.price AS v FROM dbo.sale AS s UNION ALL SELECT IIF(s.id > 0, 1, 2) FROM dbo.sale AS s;")));

        // Decimal arithmetic counts the same digits.
        Assert.Equal("decimal(38,37) arithmetic-overflow", Typed(Procedure("SELECT s.frac + CAST(NULL AS int) AS v FROM dbo.sale AS s;")).Replace(" null-literal", "", StringComparison.Ordinal));
        Assert.StartsWith("unknown: ", Typed(Procedure("SELECT s.frac + IIF(s.id > 0, 1, 2) AS v FROM dbo.sale AS s;")), StringComparison.Ordinal);
    }

    [Fact]
    public void A_bit_takes_the_other_type_and_overflows_only_a_decimal_of_places_only()
    {
        // A_bit_takes_the_other_types_and_a_string_the_number_date_or_uniqueidentifier_beside_it;
        // A_conversion_that_overflows_is_an_error_by_default_and_null_under_the_callers_options (bit beside decimal(38,38)).
        Assert.Equal("tinyint", Union("flag", "tiny"));
        Assert.Equal("decimal(9,2)", Union("flag", "price"));
        Assert.Equal("real", Union("flag", "ratio"));
        Assert.Equal("decimal(38,38) arithmetic-overflow", Union("flag", "frac"));
        Assert.Equal("bit arithmetic-overflow", Union("code", "flag"));
    }

    [Fact]
    public void A_conversion_that_may_overflow_makes_the_column_nullable_rather_than_unknown()
    {
        // A_conversion_that_overflows_is_an_error_by_default_and_null_under_the_callers_options.
        Assert.Equal("smallmoney arithmetic-overflow", Union("big", "small"));
        Assert.Equal("money arithmetic-overflow", Union("big", "fee"));
        Assert.Equal("money", Union("qty", "fee"));
        Assert.Equal("smallmoney", Union("tiny", "small"));
        Assert.Equal("datetime arithmetic-overflow", Union("rate", "stamp"));
        Assert.Equal("int arithmetic-overflow", Union("code", "qty"));
        Assert.Equal("smalldatetime arithmetic-overflow", Union("sold", "small_stamp"));
        Assert.Equal("datetime arithmetic-overflow", Union("sold", "stamp"));
    }

    [Fact]
    public void A_conversion_that_cannot_overflow_adds_nothing()
    {
        // A_conversion_that_cannot_overflow_keeps_every_value_at_its_edges;
        // A_string_that_is_not_a_value_of_the_type_is_an_error_even_under_the_callers_options (uniqueidentifier).
        Assert.Equal("smalldatetime", Union("tiny", "small_stamp"));
        Assert.Equal("datetime", Union("flag", "stamp"));
        Assert.Equal("datetime", Union("at_time", "stamp"));
        Assert.Equal("datetime2(7)", Union("sold", "moment"));
        Assert.Equal("datetime2(7)", Union("at_time", "moment"));
        Assert.Equal("datetimeoffset(3)", Union("at_time", "zoned"));
        Assert.Equal("datetimeoffset(3)", Union("stamp", "zoned"));
        Assert.Equal("datetimeoffset(7)", Union("moment", "zoned"));
        Assert.Equal("uniqueidentifier", Union("ref_text", "ref"));
    }

    [Fact]
    public void Strings_are_national_if_any_is_varying_if_any_is_and_max_if_any_is()
    {
        // Strings_are_national_if_any_is_varying_if_any_is_and_national_ones_stop_at_4000.
        Assert.Equal("nvarchar(20)", Union("code", "nkind"));
        Assert.Equal("varchar(10)", Union("fixed", "kind"));
        Assert.Equal("nvarchar(max) nullable-column", Union("note", "nkind"));
    }

    [Fact]
    public void Types_sql_server_refuses_to_combine_are_unknown_naming_both()
    {
        // Types_without_an_implicit_conversion_are_refused_with_an_operand_type_clash.
        var clash = Union("qty", "ref");
        Assert.StartsWith("unknown: ", clash, StringComparison.Ordinal);
        Assert.Contains("int and uniqueidentifier", clash, StringComparison.Ordinal);
        Assert.Contains("refuse", clash, StringComparison.Ordinal);
        Assert.Contains("date and time(3)", Union("sold", "at_time"), StringComparison.Ordinal);
        Assert.Contains("decimal(9,2) and datetime2(7)", Union("price", "moment"), StringComparison.Ordinal);

        // A value of a type not modelled is unknown by its own reason, which names it.
        var xml = Union("code", "doc");
        Assert.StartsWith("unknown: ", xml, StringComparison.Ordinal);
        Assert.Contains("xml", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Coalesce_and_case_combine_types_by_the_same_rule()
    {
        // A_conversion_that_overflows_is_an_error_by_default_and_null_under_the_callers_options (COALESCE and CASE too).
        Assert.Equal("smallmoney arithmetic-overflow", Typed(Procedure("SELECT COALESCE(s.big, s.small) AS v FROM dbo.sale AS s;")));
        Assert.Equal("smallmoney arithmetic-overflow", Typed(Procedure("SELECT CASE WHEN s.id > 0 THEN s.big ELSE s.small END AS v FROM dbo.sale AS s;")));
        Assert.Equal("decimal(38,8)", Typed(Procedure("SELECT COALESCE(s.amount, s.whole) AS v FROM dbo.sale AS s;")));
        Assert.Equal("datetimeoffset(3)", Typed(Procedure("SELECT IIF(s.id > 0, s.at_time, s.zoned) AS v FROM dbo.sale AS s;")));
        Assert.Contains("int and uniqueidentifier", Typed(Procedure("SELECT COALESCE(s.qty, s.ref) AS v FROM dbo.sale AS s;")), StringComparison.Ordinal);
        Assert.Contains("int and uniqueidentifier", Typed(Procedure("SELECT CASE WHEN s.id > 0 THEN s.qty ELSE s.ref END AS v FROM dbo.sale AS s;")), StringComparison.Ordinal);
    }

    [Fact]
    public void An_overflow_in_a_query_that_intersect_compares_away_makes_nothing_nullable()
    {
        // INTERSECT has no NULL when either side has none (Intersect_and_except_treat_nulls_as_equal_so_intersect_with_a_non_null_side_has_no_null):
        // a bigint that overflows smallmoney is NULL on the left, and no NULL is on the right. EXCEPT keeps the left side's.
        Assert.Equal("smallmoney", Typed(Procedure("SELECT s.big AS v FROM dbo.sale AS s INTERSECT SELECT s.small FROM dbo.sale AS s;")));
        Assert.Equal("smallmoney", Typed(Procedure("SELECT s.small AS v FROM dbo.sale AS s INTERSECT SELECT s.big FROM dbo.sale AS s;")));
        Assert.Equal("smallmoney arithmetic-overflow", Typed(Procedure("SELECT s.big AS v FROM dbo.sale AS s EXCEPT SELECT s.small FROM dbo.sale AS s;")));
        Assert.Equal("smallmoney", Typed(Procedure("SELECT s.small AS v FROM dbo.sale AS s EXCEPT SELECT s.big FROM dbo.sale AS s;")));
    }

    // ---- facets and value sets --------------------------------------------------------------------------------------------

    [Fact]
    public void Ranges_widen_to_cover_every_query_whose_values_convert_unchanged()
    {
        // qty is 1 to 500 (a CHECK) and tinyint 0 to 255: as int, 0 to 500. Beside decimal(9,2) of at least 0, at least 0.
        var doc = Procedure("SELECT s.qty AS v FROM dbo.sale AS s UNION ALL SELECT s.tiny FROM dbo.sale AS s;");
        Assert.Equal("int", Typed(doc));
        Assert.Equal("0", Facets(doc)?.Min?.Value);
        Assert.Equal("500", Facets(doc)?.Max?.Value);

        // frac's places are cut from 38 to 28 beside an int, so its values round and its type's bounds do not carry: the
        // column has its own type's, not qty's 500.
        doc = Procedure("SELECT s.frac AS v FROM dbo.sale AS s UNION ALL SELECT s.qty FROM dbo.sale AS s;");
        Assert.Equal("decimal(38,28)", Typed(doc));
        Assert.Equal("9999999999.9999999999999999999999999999", Facets(doc)?.Max?.Value);

        doc = Procedure("SELECT s.qty AS v FROM dbo.sale AS s UNION ALL SELECT s.price FROM dbo.sale AS s;");
        Assert.Equal("decimal(12,2)", Typed(doc));
        Assert.Equal("0", Facets(doc)?.Min?.Value);
        Assert.Equal(new IrDigits(12, 2, Facets(doc)!.Digits!.From), Facets(doc)?.Digits);
    }

    [Fact]
    public void A_query_whose_values_are_rounded_or_approximated_keeps_only_the_types_facts()
    {
        // price's CHECK says at least 0; as float or past a cut scale its values change, so only the column's type speaks.
        var doc = Procedure("SELECT s.price AS v FROM dbo.sale AS s UNION ALL SELECT s.rate FROM dbo.sale AS s;");
        Assert.Equal("float", Typed(doc));
        Assert.Null(Facets(doc)?.Min);
    }

    [Fact]
    public void Lengths_widen_to_the_longest_query()
    {
        var doc = Procedure("SELECT s.fixed AS v FROM dbo.sale AS s UNION ALL SELECT s.code FROM dbo.sale AS s;");
        Assert.Equal("varchar(10)", Typed(doc));
        Assert.Equal(10, Facets(doc)?.Length?.Value);
        Assert.False(Facets(doc)?.Length?.IsFixed);
    }

    [Fact]
    public void Value_sets_merge_across_string_types_and_drop_where_a_value_would_change()
    {
        // 'stall'/'web' (varchar) and N'auction' (nvarchar): as nvarchar(20), every one of them.
        Assert.Equal("auction|stall|web", Values(Procedure("SELECT s.kind AS v FROM dbo.sale AS s UNION ALL SELECT s.nkind FROM dbo.sale AS s;")));

        // '1'/'2' as int are numbers, not those strings: no set claimed.
        var digits = Procedure("SELECT s.digits AS v FROM dbo.sale AS s UNION ALL SELECT s.qty FROM dbo.sale AS s;");
        Assert.Equal("int arithmetic-overflow", Typed(digits));
        Assert.Null(Values(digits));

        // A 4001-character literal beside an nvarchar is cut to nvarchar(4000)'s 4000 characters: its set does not hold
        // (Strings_are_national_if_any_is_varying_if_any_is_and_national_ones_stop_at_4000,
        // A_longer_string_is_cut_to_4000_characters_beside_a_national_one_without_an_error).
        var cut = Procedure($"SELECT CASE WHEN s.id > 0 THEN '{new string('a', 4001)}' ELSE 'b' END AS v FROM dbo.sale AS s UNION ALL SELECT N'x';");
        Assert.Equal("nvarchar(4000)", Typed(cut));
        Assert.Null(Values(cut));

        // A date's set, as datetime2, would be dates where the values are datetimes: not claimed.
        Assert.Null(Values(Procedure("SELECT s.sold AS v FROM dbo.sale AS s UNION ALL SELECT s.moment FROM dbo.sale AS s;")));

        // tier is 1 or 2 as decimal(38,0); 2.5 beside it is rounded to 3, so neither set nor literal is claimed.
        var tier = Procedure("SELECT s.tier AS v FROM dbo.sale AS s UNION ALL SELECT 2.5;");
        Assert.Equal("decimal(38,0)", Typed(tier));
        Assert.Null(Values(tier));

        // Within 38 digits nothing is rounded: 1, 2 and 2.5.
        Assert.Equal("1|2|2.5", Values(Procedure("SELECT CASE WHEN s.id > 0 THEN 1 ELSE 2 END AS v FROM dbo.sale AS s UNION ALL SELECT 2.5;")));
    }

    [Fact]
    public void A_case_of_literals_past_38_digits_claims_no_literal_a_cut_scale_would_round()
    {
        // A 38-digit integer literal is numeric(38,0), so 2.5 beside it is rounded to 3.
        var doc = Procedure("SELECT CASE WHEN s.id > 0 THEN 12345678901234567890123456789012345678 ELSE 2.5 END AS v FROM dbo.sale AS s;");
        Assert.Equal("numeric(38,0)", Typed(doc));
        Assert.Null(Values(doc));
    }
}
