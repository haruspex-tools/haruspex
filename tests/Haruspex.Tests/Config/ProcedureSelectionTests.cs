using System.Text;
using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;

namespace Haruspex.Tests.Config;

/// <summary>
/// Selection per schema (docs/decisions/0010-procedures-are-selected-per-schema.md): each schema's block selects among its
/// procedures by their own names.
/// </summary>
public class ProcedureSelectionTests
{
    private static readonly ObjectName[] Procedures =
    [
        new("sales", "get_order"),
        new("sales", "list_orders"),
        new("sales", "list_orders_internal"),
        new("hr", "list_people"),
        new("Ledger.", "GetEntryList"),
        new("Ledger.", "Get.Dotted"),
    ];

    private static Outcome<EquatableArray<ObjectName>> Select(string schemas)
    {
        var read = ConfigReader.Read(Encoding.UTF8.GetBytes($$"""{ "schemas": {{schemas}} }"""), "haruspex.json", Path.GetTempPath());
        Assert.Empty(read.Diagnostics);
        return ProcedureSelection.Select(Procedures, read.Expected());
    }

    private static string[] Names(Outcome<EquatableArray<ObjectName>> result)
    {
        Assert.Empty(result.Diagnostics);
        return [.. result.Expected().Select(p => p.ToString())];
    }

    [Fact]
    public void A_schema_s_include_selects_its_procedures_by_name_ignoring_case() =>
        Assert.Equal(["sales.get_order", "hr.list_people"], Names(Select("""{ "sales": { "include": ["GET_*"] }, "HR": { "include": ["*"] } }""")));

    [Fact]
    public void Exclude_wins_over_include() =>
        Assert.Equal(["sales.get_order", "sales.list_orders"], Names(Select("""{ "sales": { "include": ["*"], "exclude": ["*_internal"] } }""")));

    [Fact]
    public void A_schema_key_is_its_literal_name_and_a_dot_in_a_pattern_is_literal() =>
        Assert.Equal(["[Ledger.].[Get.Dotted]"], Names(Select("""{ "Ledger.": { "include": ["get.*"] } }""")));

    [Theory]
    [InlineData("get_*", "get_order", true)]
    [InlineData("get_?", "get_x", true)]
    [InlineData("get_?", "get_xy", false)]
    [InlineData("GET_ORDER", "get_order", true)]
    [InlineData("[a*b]", "a*b", true)]
    [InlineData("[a*b]", "axxb", false)]
    [InlineData("[a]]b]", "a]b", true)]
    [InlineData("a.b", "a.b", true)]
    [InlineData("a.b", "axb", false)]
    public void A_name_pattern_matches_the_object_s_own_name(string pattern, string name, bool matches)
    {
        Assert.True(NamePattern.TryParse(pattern, out var parsed, out var error), error);
        Assert.Equal(matches, parsed.Matches(name));
    }

    [Fact]
    public void A_schema_without_include_selects_nothing() =>
        Assert.Empty(Names(Select("""{ "sales": { "prefix": "Sales" } }""")));

    [Fact]
    public void Nothing_is_selected_without_a_schema_block() =>
        Assert.Empty(Names(Select("{}")));

    [Fact]
    public void An_include_that_matches_nothing_names_the_schema_and_the_pattern()
    {
        var result = Select("""{ "sales": { "include": ["*", "get_ordr"] } }""");

        Assert.Equal("haruspex.json:1:44: error HAR4002: schemas.sales.include[1]: 'get_ordr' matches no procedure in the schema \"sales\". Correct it, or remove it.", Assert.Single(result.Diagnostics).ToString());
    }

    [Fact]
    public void An_exclude_that_excludes_nothing_is_an_error()
    {
        var result = Select("""{ "sales": { "include": ["get_*"], "exclude": ["*_internal"] } }""");

        Assert.Equal(
            "haruspex.json:1:61: error HAR4003: schemas.sales.exclude[0]: '*_internal' excludes nothing: it matches no procedure that \"include\" selects in the schema \"sales\". Remove it.",
            Assert.Single(result.Diagnostics).ToString());
    }

    [Fact]
    public void A_schema_whose_exclude_removes_everything_is_an_error()
    {
        var result = Select("""{ "sales": { "include": ["list_*"], "exclude": ["list_*"] } }""");

        Assert.IsType<Failed<EquatableArray<ObjectName>>>(result);
        Assert.Equal(
            "haruspex.json:1:16: error HAR4004: schemas.sales: the schema \"sales\" selects nothing: its \"exclude\" removes every procedure its \"include\" selects. Narrow the exclude, or remove the block.",
            Assert.Single(result.Diagnostics).ToString());
    }

    [Fact]
    public void A_schema_key_that_names_no_schema_of_the_project_is_an_error()
    {
        var result = Select("""{ "[Ledger.]": { "include": ["*"] } }""");

        Assert.Equal(
            "haruspex.json:1:16: error HAR4001: schemas[\"[Ledger.]\"]: the project has no procedure in a schema named \"[Ledger.]\". A schema key is the schema's name as it is, without brackets.",
            Assert.Single(result.Diagnostics).ToString());
    }

    [Fact]
    public void Command_line_patterns_override_the_includes_but_not_the_excludes()
    {
        var read = ConfigReader.Read(Encoding.UTF8.GetBytes("""{ "schemas": { "sales": { "include": ["get_*"], "exclude": ["*_internal"] } } }"""), "haruspex.json", Path.GetTempPath());

        var selection = ProcedureSelection.Select(Procedures, [ObjectPattern.Parse("sales.list_*")], read.Expected());

        Assert.Equal(["sales.list_orders"], selection.Expected().Select(p => p.ToString()));
    }

    [Fact]
    public void A_command_line_pattern_that_selects_nothing_is_an_error_and_every_such_pattern_is_reported()
    {
        var read = ConfigReader.Read(Encoding.UTF8.GetBytes("""{ "schemas": { "sales": { "include": ["get_*"], "exclude": ["*_internal"] } } }"""), "haruspex.json", Path.GetTempPath());

        var selection = ProcedureSelection.Select(
            Procedures, [ObjectPattern.Parse("sales.list_*"), ObjectPattern.Parse("hr.nobody"), ObjectPattern.Parse("sales.*_internal")], read.Expected());

        Assert.IsType<Failed<EquatableArray<ObjectName>>>(selection);
        Assert.Equal(
            [
                "haruspex: error HAR4005: --include 'hr.nobody' matches no procedure. Correct it, or remove it.",
                "haruspex: error HAR4006: --include 'sales.*_internal' matches only procedures that schemas.sales.exclude leaves out, so it selects nothing.",
            ],
            selection.Diagnostics.Select(p => p.ToString()));
    }
}
