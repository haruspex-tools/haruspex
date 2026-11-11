using System.Text.RegularExpressions;

namespace Haruspex.Tests.Config;

/// <summary>The naming rules: replacements, then the schema's prefix, then the case style. The schema itself is never part of a name.</summary>
public class NamingRulesTests
{
    private static NamingReplacement Rule(string pattern, string with) => new(new Regex(pattern, RegexOptions.NonBacktracking), with, null);

    private static NamingRules Rules(NamingCase style = NamingCase.Pascal, string prefix = "", params (string Pattern, string With)[] replace) =>
        new([.. replace.Select(r => Rule(r.Pattern, r.With))], style, prefix);

    private static string? Name(NamingRules rules, string name) => rules.Name(name, out _);

    [Theory]
    [InlineData("get_order", "GetOrder")]
    [InlineData("stock$level$history", "StockLevelHistory")]
    [InlineData("person#", "Person")]
    [InlineData("placedAt", "PlacedAt")]
    [InlineData("GetPropertyList", "GetPropertyList")]
    [InlineData("2fa_codes", "_2faCodes")]
    public void Pascal_breaks_words_at_every_sigil_and_leaves_the_schema_out(string name, string expected) =>
        Assert.Equal(expected, Name(NamingRules.Default, name));

    [Theory]
    [InlineData(NamingCase.Pascal, "Audit", "get_order", "AuditGetOrder")]
    [InlineData(NamingCase.Pascal, "core_v2", "get_order", "CoreV2GetOrder")]
    [InlineData(NamingCase.Camel, "Audit", "get_order", "auditGetOrder")]
    [InlineData(NamingCase.Preserve, "Audit", "get_order", "Auditget_order")]
    [InlineData(NamingCase.Preserve, "audit$", "get_order", "audit$get_order")]
    public void A_prefix_goes_in_front_before_the_case_rules(NamingCase style, string prefix, string name, string expected) =>
        Assert.Equal(expected, Name(Rules(style, prefix), name));

    [Fact]
    public void Replacements_run_in_order_before_the_case()
    {
        var rules = Rules(NamingCase.Pascal, "", ("#$", "Row"), ("Row$", "Record"));

        // #$ → Row first, then Row$ → Record: the second sees the first's result.
        Assert.Equal("PersonRecord", Name(rules, "person#"));
    }

    [Fact]
    public void A_replacement_sees_the_name_without_the_prefix() =>
        Assert.Equal("AuditOrder", Name(Rules(NamingCase.Pascal, "Audit", ("^get_", "")), "get_order"));

    [Fact]
    public void A_replacement_can_use_groups() =>
        Assert.Equal("ReportVersion2", Name(Rules(NamingCase.Pascal, "", ("^(\\w+)_v(\\d)$", "$1Version$2")), "report_v2"));

    [Fact]
    public void A_schema_s_naming_merges_over_the_top_level()
    {
        var top = Rules(NamingCase.Pascal, "", ("#$", "Row"));
        var schema = new SchemaNaming([Rule("Row$", "Entry")], NamingCase.Camel);

        var merged = top.For(schema, "Audit");

        // Its case replaces the top level's; its replacements run after the top level's.
        Assert.Equal(NamingCase.Camel, merged.Case);
        Assert.Equal("auditPersonEntry", Name(merged, "person#"));
        Assert.Equal("AuditPersonRow", Name(top.For(null, "Audit"), "person#"));
        Assert.Equal("auditPersonRow", Name(top.For(new SchemaNaming([], NamingCase.Camel), "Audit"), "person#"));
    }

    [Theory]
    [InlineData("get_order", "getOrder")]
    [InlineData("GetOrder", "getOrder")]
    [InlineData("stock$level$history", "stockLevelHistory")]
    public void Camel_lowers_the_first_letter(string name, string expected) =>
        Assert.Equal(expected, Name(Rules(NamingCase.Camel), name));

    [Theory]
    [InlineData("stock$level$history")]
    [InlineData("get_order")]
    [InlineData("$x")]
    public void Preserve_keeps_the_name_as_it_is(string name) =>
        Assert.Equal(name, Name(Rules(NamingCase.Preserve), name));

    [Fact]
    public void Preserve_with_a_name_that_is_not_an_identifier_is_an_error()
    {
        Assert.Null(Rules(NamingCase.Preserve).Name("person#", out var why));

        Assert.Equal(
            "\"case\": \"preserve\" gives 'person#', which is not a valid identifier (an ASCII letter, _ or $, then letters, digits, _ or $). Add a replacement, or give it a name in \"procedures\".",
            why);
    }

    [Fact]
    public void Preserve_with_a_replacement_that_makes_a_valid_name_succeeds() =>
        Assert.Equal("person_row", Name(Rules(NamingCase.Preserve, "", ("#$", "_row")), "person#"));

    [Fact]
    public void A_reserved_word_is_an_error()
    {
        Assert.Null(Rules(NamingCase.Camel).Name("delete", out var why));

        Assert.Equal("the rules give 'delete', which is a reserved word in TypeScript. Give it a name in \"procedures\".", why);
    }

    [Fact]
    public void A_name_with_no_letters_or_digits_is_an_error()
    {
        Assert.Null(Rules(NamingCase.Pascal).Name("#", out var why));

        Assert.Equal("'#' has no letters or digits to make a name from. Give it a name in \"procedures\".", why);
    }

    [Theory]
    [InlineData("Lines", "Line")]
    [InlineData("Categories", "Category")]
    [InlineData("Boxes", "Box")]
    [InlineData("Address", "AddressItem")]
    [InlineData("Data", "DataItem")]
    public void Singular_names_one_item_of_a_list(string plural, string expected) =>
        Assert.Equal(expected, NamingRules.Singular(plural));

    [Theory]
    [InlineData("unit price", "UnitPrice")]
    [InlineData("#", "Unnamed")]
    public void A_key_becomes_a_pascal_part(string key, string expected) =>
        Assert.Equal(expected, NamingRules.Part(key));
}
