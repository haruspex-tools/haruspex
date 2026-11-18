
namespace Haruspex.Tests.Config;

public partial class ConfigApplicationTests
{
    /// <summary>A procedure of two result sets: numbers, then customers.</summary>
    private const string TwoSets = "CREATE PROCEDURE dbo.p AS SELECT 1 AS a;\nSELECT c.customer_id AS id FROM sales.customer AS c;\nGO\n";

    [Fact]
    public void Named_sets_are_recorded_in_order_and_their_types_are_named_after_them()
    {
        var doc = Apply(TwoSets, """{ "procedures": { "dbo.p": { "sets": ["numbers", "customers"] } } }""").Document;

        Assert.Equal(["numbers", "customers"], doc.Objects[0].Results!.Select(r => r.Name));
        Assert.Equal(["PCustomersRow", "PNumbersRow"], doc.Types.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void One_named_set_names_its_type_after_it_too()
    {
        var doc = Apply(GetCustomer, """{ "procedures": { "sales.get_customer": { "sets": ["customers"] } } }""").Document;

        Assert.Equal("customers", Assert.Single(doc.Objects[0].Results!).Name);
        Assert.Equal(["GetCustomerCustomersLine", "GetCustomerCustomersRow"], doc.Types.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_named_set_s_types_keep_their_names_when_a_set_is_added_after_it()
    {
        var one = Apply("CREATE PROCEDURE dbo.p AS SELECT 1 AS a;\nGO\n", """{ "procedures": { "dbo.p": { "sets": ["numbers"] } } }""").Document;
        var two = Apply(TwoSets, """{ "procedures": { "dbo.p": { "sets": ["numbers", "customers"] } } }""").Document;

        Assert.Equal(["PNumbersRow"], one.Types.Keys);
        Assert.Contains("PNumbersRow", two.Types.Keys);
    }

    [Fact]
    public void A_path_s_name_wins_over_the_set_s_and_is_marked_declared()
    {
        var doc = Apply(TwoSets, """{ "procedures": { "dbo.p": { "sets": ["numbers", "customers"], "paths": { "results[1][]": { "name": "Customer" } } } } }""").Document;

        Assert.Equal(["Customer", "PNumbersRow"], doc.Types.Keys.Order(StringComparer.Ordinal));
        Assert.True(Type(doc, "Customer").NameDeclared);
        Assert.False(Type(doc, "PNumbersRow").NameDeclared);
    }

    [Fact]
    public void Fewer_names_than_sets_is_an_error_naming_both_counts()
    {
        var run = Apply(TwoSets, """{ "procedures": { "dbo.p": { "sets": ["numbers"] } } }""");

        Assert.Equal("haruspex.json:1:38: error HAR4112: procedures[\"dbo.p\"].sets: names one result set, but dbo.p returns 2 result sets. Name every set it returns, in order, or none.", run.Error);
    }

    [Fact]
    public void Names_for_a_procedure_that_returns_no_sets_is_an_error()
    {
        var run = Apply("CREATE PROCEDURE dbo.p AS UPDATE sales.customer SET level = 1;\nGO\n", """{ "procedures": { "dbo.p": { "sets": ["rows"] } } }""");

        Assert.Equal("haruspex.json:1:38: error HAR4112: procedures[\"dbo.p\"].sets: names one result set, but dbo.p returns none. Name every set it returns, in order, or none.", run.Error);
    }

    [Fact]
    public void Names_for_results_that_cannot_be_listed_is_an_error_that_says_to_declare_them()
    {
        var run = Apply("CREATE PROCEDURE dbo.p @sql nvarchar(max) AS EXEC sp_executesql @sql;\nGO\n", """{ "procedures": { "dbo.p": { "sets": ["rows"] } } }""");

        Assert.Equal("haruspex.json:1:38: error HAR4112: procedures[\"dbo.p\"].sets: names one result set, but the result sets of dbo.p can't be listed, so there are none to name. Declare its results under \"results\", or remove \"sets\".", run.Error);
    }

    [Fact]
    public void Declared_results_can_be_named_in_the_same_entry()
    {
        var doc = Apply("CREATE PROCEDURE dbo.p @sql nvarchar(max) AS EXEC sp_executesql @sql;\nGO\n", """
            { "procedures": { "dbo.p": {
                "results": [{ "rowset": { "type": "object", "properties": { "id": { "type": "integer" } } } }, { "json": { "type": "object", "properties": { "n": { "type": "integer" } }, "required": ["n"] } }],
                "why": "dynamic SQL", "sets": ["rows", "totals"] } } }
            """).Document;

        Assert.Equal(["rows", "totals"], doc.Objects[0].Results!.Select(r => r.Name));
        Assert.Equal(["PRowsRow", "PTotalsRow"], doc.Types.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Names_for_a_procedure_that_is_not_selected_is_an_error()
    {
        var run = Apply(GetCustomer + TwoSets, """{ "procedures": { "dbo.p": { "sets": ["numbers", "customers"] } } }""", include: "sales.*");

        Assert.StartsWith("haruspex.json:1:19: error HAR4102: procedures[\"dbo.p\"]: the procedure dbo.p is not selected", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Unnamed_sets_have_no_names_and_types_named_by_place()
    {
        var doc = Apply(TwoSets, "{}").Document;

        Assert.All(doc.Objects[0].Results!, r => Assert.Null(r.Name));
        Assert.Equal(["PResult1Row", "PResult2Row"], doc.Types.Keys.Order(StringComparer.Ordinal));
    }
}
