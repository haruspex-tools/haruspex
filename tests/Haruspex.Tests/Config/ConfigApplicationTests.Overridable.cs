namespace Haruspex.Tests.Config;

public partial class ConfigApplicationTests
{
    [Fact]
    public void An_override_of_a_procedure_that_does_not_exist_is_an_error()
    {
        var run = Apply(GetCustomer, """{ "procedures": { "sales.get_customr": { "name": "X" } } }""");

        Assert.Equal("haruspex.json:1:19: error: procedures[\"sales.get_customr\"]: there is no procedure sales.get_customr in the project.", run.Error);
    }

    [Fact]
    public void An_override_of_a_procedure_that_is_not_selected_is_an_error()
    {
        var run = Apply(GetCustomer + "CREATE PROCEDURE sales.other AS SELECT 1 AS a;\nGO\n", """{ "procedures": { "sales.other": { "name": "X" } } }""", include: "sales.get_*");

        Assert.Equal(
            "haruspex.json:1:19: error: procedures[\"sales.other\"]: the procedure sales.other is not selected, so it can't be overridden. Overrides apply only to the procedures its schema's \"include\" selects and \"exclude\" leaves in.",
            run.Error);
    }

    [Fact]
    public void An_override_of_a_procedure_a_one_off_include_left_out_is_skipped()
    {
        var run = Apply(GetCustomer + "CREATE PROCEDURE sales.other AS SELECT 1 AS a;\nGO\n", """{ "procedures": { "sales.other": { "name": "X" } } }""",
            include: "sales.get_*", selectedByFile: ["sales.get_customer", "sales.other"]);

        Assert.Empty(run.Problems);
    }

    [Fact]
    public void Two_overrides_of_one_procedure_written_in_different_case_are_an_error()
    {
        var run = Apply(GetCustomer, "{ \"procedures\": {\n\"sales.get_customer\": { \"name\": \"A\" },\n\"SALES.Get_Customer\": { \"name\": \"B\" } } }");

        Assert.Equal("haruspex.json:3:1: error: procedures[\"SALES.Get_Customer\"]: sales.get_customer already has overrides, at line 2; names are compared ignoring case, as SQL Server compares them. Merge the two.", run.Error);
    }

    [Fact]
    public void An_override_matches_its_procedure_ignoring_case() =>
        Assert.Equal("X", Apply(GetCustomer, """{ "procedures": { "SALES.GET_CUSTOMER": { "name": "X" } } }""").Document.Objects[0].Name);
}
