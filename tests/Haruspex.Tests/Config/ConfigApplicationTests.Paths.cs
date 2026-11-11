namespace Haruspex.Tests.Config;

public partial class ConfigApplicationTests
{
    [Fact]
    public void A_path_that_does_not_exist_is_an_error_listing_the_keys_there()
    {
        var run = Apply(GetCustomer, """{ "procedures": { "sales.get_customer": { "paths": { "[].nmae": { "nullable": false, "why": "x" } } } } }""");

        Assert.Equal(
            "haruspex.json:1:54: error: procedures[\"sales.get_customer\"].paths[\"[].nmae\"]: the path results[0][].nmae does not exist: the object at results[0][] has no key \"nmae\". Its keys are \"id\", \"name\", \"status\", \"level\", \"notes\", \"lines\".",
            run.Error);
    }

    [Fact]
    public void A_path_through_the_wrong_kind_of_value_is_an_error()
    {
        var run = Apply(GetCustomer, """{ "procedures": { "sales.get_customer": { "paths": { "[].name[]": { "nullable": false, "why": "x" } } } } }""");

        Assert.Equal(
            "haruspex.json:1:54: error: procedures[\"sales.get_customer\"].paths[\"[].name[]\"]: the path results[0][].name[] does not exist: the value at results[0][].name is a string (nvarchar(40)), so it has no items ([]).",
            run.Error);
    }

    [Fact]
    public void A_path_into_unknown_results_is_an_error()
    {
        var run = Apply("CREATE PROCEDURE dbo.p AS EXEC ('SELECT 1');\nGO\n", """{ "procedures": { "dbo.p": { "paths": { "[]": { "name": "X" } } } } }""");

        Assert.Equal("haruspex.json:1:41: error: procedures[\"dbo.p\"].paths[\"[]\"]: the results of dbo.p are unknown (see the unknowns in the IR), so no path in them exists.", run.Error);
    }

    [Fact]
    public void A_path_through_a_union_is_an_error()
    {
        // An object and a nullable array, ABSENT ON NULL: an array whose items are either, as JSON text in a rowset.
        var run = Apply("CREATE PROCEDURE dbo.p AS SELECT json_array(json_object('a': 1), (SELECT l.sku FROM sales.line AS l FOR JSON PATH)) AS u;\nGO\n",
            """{ "procedures": { "dbo.p": { "paths": { "[].u<json>[].a": { "nullable": false, "why": "x" } } } } }""");

        Assert.Equal("haruspex.json:1:41: error: procedures[\"dbo.p\"].paths[\"[].u<json>[].a\"]: the path results[0][].u<json>[].a passes through a value of several types at results[0][].u<json>[], and a path can't choose between them.", run.Error);
    }

    [Fact]
    public void Naming_something_that_is_not_an_object_is_an_error()
    {
        var run = Apply(GetCustomer, """{ "procedures": { "sales.get_customer": { "paths": { "[].lines": { "name": "Lines" } } } } }""");

        Assert.Equal(
            "haruspex.json:1:54: error: procedures[\"sales.get_customer\"].paths[\"[].lines\"]: only an object type can be named, and the value at results[0][].lines is an array. To name its items, use the path \"[].lines[]\".",
            run.Error);
    }
}
