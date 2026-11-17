using Haruspex.Diagnostics;
using Haruspex.Ir;

namespace Haruspex.Tests.Config;

public partial class ConfigApplicationTests
{
    [Fact]
    public void Default_names_leave_the_schema_out_and_build_nested_names_on_the_procedure()
    {
        var doc = Apply(GetCustomer, "{}").Document;

        var procedure = Assert.Single(doc.Objects);
        Assert.Equal(("sales.get_customer", "GetCustomer"), (procedure.Id, procedure.Name));
        Assert.Equal(["GetCustomerLine", "GetCustomerRow"], doc.Types.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(new IrTypeIdentity(IrTypeOwner.Procedure, "sales.get_customer", "results[0][]"), Type(doc, "GetCustomerRow").Identity);
        Assert.Equal(new IrTypeIdentity(IrTypeOwner.Procedure, "sales.get_customer", "results[0][].lines[]"), Type(doc, "GetCustomerLine").Identity);
    }

    [Fact]
    public void Naming_rules_apply_to_the_procedure_and_its_types()
    {
        var doc = Apply(GetCustomer, """{ "naming": { "case": "camel" } }""").Document;

        Assert.Equal("getCustomer", doc.Objects[0].Name);
        Assert.Equal(["getCustomerLine", "getCustomerRow"], doc.Types.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_schema_s_prefix_and_naming_apply_to_its_procedures_and_their_types()
    {
        var doc = Apply(GetCustomer, """
            { "naming": { "replace": [{ "pattern": "^get_", "with": "" }] },
              "schemas": { "sales": { "prefix": "Sales", "naming": { "case": "camel", "replace": [{ "pattern": "customer", "with": "client" }] } } } }
            """).Document;

        Assert.Equal("salesClient", doc.Objects[0].Name);
        Assert.Equal(["salesClientLine", "salesClientRow"], doc.Types.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_name_override_names_the_procedure_and_the_types_built_on_it()
    {
        var doc = Apply(GetCustomer, """{ "procedures": { "sales.get_customer": { "name": "Customers" } } }""").Document;

        Assert.Equal("Customers", doc.Objects[0].Name);
        Assert.Equal(["CustomersLine", "CustomersRow"], doc.Types.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_path_name_override_names_a_nested_type()
    {
        var doc = Apply(GetCustomer, """{ "procedures": { "sales.get_customer": { "paths": { "[].lines[]": { "name": "CustomerLine" }, "[]": { "name": "Customer" } } } } }""").Document;

        Assert.Equal(["Customer", "CustomerLine"], doc.Types.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_name_override_the_rules_already_give_is_a_warning()
    {
        var run = Apply(GetCustomer, """
            { "procedures": { "sales.get_customer": { "name": "GetCustomer",
              "paths": { "[].lines[]": { "name": "GetCustomerLine" } } } } }
            """);

        Assert.Equal(
            [
                "haruspex.json:1:51: warning HAR4304: procedures[\"sales.get_customer\"].name: the name \"GetCustomer\" is what the naming rules give sales.get_customer already. Remove it.",
                "haruspex.json:2:14: warning HAR4304: procedures[\"sales.get_customer\"].paths[\"[].lines[]\"]: the name \"GetCustomerLine\" is what the naming rules give the type at results[0][].lines[] already. Remove it.",
            ],
            run.Problems);
        Assert.IsType<Succeeded<IrDocument>>(run.Applied);
    }

    [Fact]
    public void Two_procedures_whose_names_clash_are_an_error_naming_both()
    {
        var run = Apply("CREATE PROCEDURE dbo.person AS SELECT 1 AS a;\nGO\nCREATE PROCEDURE [dbo].[person#] AS SELECT 2 AS a;\nGO\n", "{}");

        Assert.IsType<Failed<IrDocument>>(run.Applied);
        Assert.Equal(
            "haruspex.json: error HAR4303: \"Person\" would name both the procedure dbo.person and the procedure dbo.person#. Generated names must not clash: add a naming \"replace\" rule for where their names differ (nothing against \"#\"), such as { \"pattern\": \"#\", \"with\": \"_hash_\" } with a word of your own for \"hash\", or give one of them its own name with a \"name\" override in \"procedures\".",
            run.Error);
    }

    [Theory]
    [InlineData("order$lines", "order#lines", "(\"#\" against \"$\")", "{ \"pattern\": \"#\", \"with\": \"_hash_\" }")]
    [InlineData("order_lines", "order__lines", "(\"__\" against \"_\")", "{ \"pattern\": \"__\", \"with\": \"_underscore_underscore_\" }")]
    [InlineData("order-lines", "order lines", "(\" \" against \"-\")", "{ \"pattern\": \" \", \"with\": \"_space_\" }")]
    [InlineData("order$lines", "order^lines", "(\"$\" against \"^\")", "{ \"pattern\": \"\\\\$\", \"with\": \"_dollar_\" }")]
    public void A_clash_s_hint_is_a_rule_built_from_where_the_names_differ_and_the_rule_resolves_it(string one, string other, string where, string rule)
    {
        // Two names one case style makes the same, which differ only in characters it drops. The rule the hint suggests
        // replaces the one side's characters with a word, and applied to the config it removes the clash.
        var procedures = $"CREATE PROCEDURE [dbo].[{one}] AS SELECT 1 AS a;\nGO\nCREATE PROCEDURE [dbo].[{other}] AS SELECT 2 AS a;\nGO\n";

        var error = Apply(procedures, "{}").Error;
        Assert.Contains($"add a naming \"replace\" rule for where their names differ {where}, such as {rule} with a word of your own for ", error, StringComparison.Ordinal);

        var suggested = System.Text.RegularExpressions.Regex.Match(error, @"such as (\{ ""pattern"": "".*?"", ""with"": "".*?"" \})").Groups[1].Value;
        var resolved = Apply(procedures, $$"""{ "naming": { "replace": [{{suggested}}] } }""");
        Assert.Empty(resolved.Problems);
        Assert.Equal(2, resolved.Document.Objects.Select(o => o.Name).Distinct().Count());
    }

    [Fact]
    public void A_clash_s_hint_goes_to_the_schema_s_own_rules_when_it_has_some_and_resolves_it_there()
    {
        // A rule added after the schema's own runs on what they leave: here "order lines" against "order#lines".
        const string Procedures = "CREATE PROCEDURE [dbo].[order$lines] AS SELECT 1 AS a;\nGO\nCREATE PROCEDURE [dbo].[order#lines] AS SELECT 2 AS a;\nGO\n";
        const string Own = """{ "pattern": "\\$", "with": " " }""";

        var error = Apply(Procedures, $$"""{ "schemas": { "dbo": { "include": ["*"], "naming": { "replace": [{{Own}}] } } } }""").Error;
        Assert.Contains("add a \"replace\" rule to the \"naming\" of schemas.dbo for where their names differ (\"#\" against \" \"), such as { \"pattern\": \"#\", \"with\": \"_hash_\" }", error, StringComparison.Ordinal);

        var resolved = Apply(Procedures, $$"""{ "schemas": { "dbo": { "include": ["*"], "naming": { "replace": [{{Own}}, { "pattern": "#", "with": "_hash_" }] } } } }""");
        Assert.Equal(["OrderHashLines", "OrderLines"], resolved.Document.Objects.Select(o => o.Name));
    }

    [Theory]
    [InlineData("""{ "naming": { "replace": [{ "pattern": "#$", "with": "" }] } }""", "their names are both \"person\" once the \"replace\" rules have run, so no further rule tells them apart")]
    [InlineData("""{ "naming": { "replace": [{ "pattern": "^p(.*)#$", "with": "P$1" }] } }""", "no \"replace\" rule tells \"person\" from \"Person\" without rewriting letters or digits")]
    public void A_clash_no_single_rule_can_resolve_says_so_and_suggests_none(string config, string why)
    {
        var error = Apply("CREATE PROCEDURE dbo.person AS SELECT 1 AS a;\nGO\nCREATE PROCEDURE [dbo].[person#] AS SELECT 2 AS a;\nGO\n", config).Error;

        Assert.EndsWith($"Generated names must not clash: give one of them its own name with a \"name\" override in \"procedures\" ({why}).", error, StringComparison.Ordinal);
        Assert.DoesNotContain("such as", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_replacement_rule_resolves_a_clash_from_the_name_itself()
    {
        var doc = Apply("CREATE PROCEDURE dbo.person AS SELECT 1 AS a;\nGO\nCREATE PROCEDURE [dbo].[person#] AS SELECT 2 AS a;\nGO\n",
            """{ "naming": { "replace": [{ "pattern": "#$", "with": "Entry" }] } }""").Document;

        Assert.Equal(["Person", "PersonEntry"], doc.Objects.Select(o => o.Name));
    }

    [Fact]
    public void Procedures_of_two_schemas_whose_names_clash_are_an_error_and_a_prefix_resolves_it()
    {
        const string Two = "CREATE PROCEDURE sales.get_customer2 AS SELECT 1 AS a;\nGO\nCREATE SCHEMA audit;\nGO\nCREATE PROCEDURE audit.get_customer2 AS SELECT 2 AS a;\nGO\n";

        // A prefix tells two schemas apart, so that is the advice here.
        Assert.Equal(
            "haruspex.json: error HAR4303: \"GetCustomer2\" would name both the procedure audit.get_customer2 and the procedure sales.get_customer2. Generated names must not clash: give one of their schemas a \"prefix\" in \"schemas\", or give one of them its own name with a \"name\" override in \"procedures\".",
            Apply(Two, "{}").Error);

        // With a block for a schema, the problem points at it, where the prefix would go.
        Assert.StartsWith("haruspex.json:1:16: error HAR4303: schemas.sales: \"GetCustomer2\" would name both", Apply(Two, """{ "schemas": { "sales": { "include": ["*"] } } }""").Error, StringComparison.Ordinal);
        Assert.Equal(["AuditGetCustomer2", "GetCustomer2"], Apply(Two, """{ "schemas": { "audit": { "prefix": "Audit" } } }""").Document.Objects.Select(o => o.Name));
    }

    [Fact]
    public void A_name_the_rules_cannot_make_points_at_the_naming_that_decides_it()
    {
        var run = Apply("CREATE PROCEDURE [dbo].[person#] AS SELECT 1 AS a;\nGO\n", "{\n  \"naming\": { \"case\": \"preserve\" }\n}");

        Assert.Equal(
            "haruspex.json:2:13: error HAR4301: naming: the procedure dbo.person# cannot be named: \"case\": \"preserve\" gives 'person#', which is not a valid identifier (an ASCII letter, _ or $, then letters, digits, _ or $). Add a replacement, or give it a name in \"procedures\".",
            run.Error);
    }

    [Fact]
    public void A_nested_type_that_clashes_with_another_procedure_is_an_error_naming_both()
    {
        var run = Apply(GetCustomer + "CREATE PROCEDURE sales.get_customer_line AS SELECT 1 AS a;\nGO\n", "{}");

        Assert.StartsWith(
            "haruspex.json: error HAR4303: \"GetCustomerLine\" would name both the procedure sales.get_customer_line and the type at results[0][].lines[] in sales.get_customer.",
            run.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_clash_is_resolved_by_an_override()
    {
        var doc = Apply(GetCustomer + "CREATE PROCEDURE sales.get_customer_line AS SELECT 1 AS a;\nGO\n",
            """{ "procedures": { "sales.get_customer_line": { "name": "CustomerLine" } } }""").Document;

        Assert.Equal(["GetCustomer", "CustomerLine"], doc.Objects.Select(o => o.Name));
        Assert.Contains("GetCustomerLine", doc.Types.Keys);
    }

    [Fact]
    public void Adding_a_procedure_never_renames_another()
    {
        var alone = Apply(GetCustomer, "{}").Document;
        var withAnother = Apply(GetCustomer + "CREATE PROCEDURE sales.get_line AS SELECT 1 AS a;\nGO\n", "{}").Document;

        Assert.Equal(alone.Types.Keys.Order(StringComparer.Ordinal), withAnother.Types.Keys.Where(k => k.StartsWith("GetCustomer", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
    }

    /// <summary>Within one procedure a number still tells two types apart: it depends only on that procedure's shape.</summary>
    [Fact]
    public void Two_types_of_one_procedure_with_one_name_are_numbered_within_it()
    {
        var doc = Apply("CREATE PROCEDURE dbo.p AS SELECT c.customer_id AS [row.id] FROM sales.customer AS c FOR JSON PATH;\nGO\n", "{}").Document;

        // The rows are PRow; the nested object "row" would be P + Row too.
        Assert.Equal(["PRow", "PRow2"], doc.Types.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("PRow2", Assert.IsType<IrRef>(Field(doc, "PRow", "row").Type).Name);
    }

    [Fact]
    public void A_name_override_that_clashes_is_an_error()
    {
        var run = Apply(GetCustomer + "CREATE PROCEDURE sales.other AS SELECT 1 AS a;\nGO\n",
            """{ "procedures": { "sales.other": { "name": "GetCustomerRow" } } }""");

        // It points at the override, the setting to change.
        Assert.StartsWith("haruspex.json:1:44: error HAR4303: procedures[\"sales.other\"].name: \"GetCustomerRow\" would name both the procedure sales.other and the type at results[0][] in sales.get_customer.", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Several_result_sets_are_numbered_and_a_path_names_one()
    {
        var doc = Apply("CREATE PROCEDURE dbo.p AS SELECT 1 AS a;\nSELECT c.customer_id AS id FROM sales.customer AS c;\nGO\n",
            """{ "procedures": { "dbo.p": { "paths": { "results[1][]": { "name": "Customer" } } } } }""").Document;

        Assert.Equal(["Customer", "PResult1Row"], doc.Types.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_path_without_its_result_set_is_an_error_when_there_are_several()
    {
        var run = Apply("CREATE PROCEDURE dbo.p AS SELECT 1 AS a;\nSELECT 2 AS b;\nGO\n",
            """{ "procedures": { "dbo.p": { "paths": { "[]": { "name": "X" } } } } }""");

        Assert.Equal("haruspex.json:1:41: error HAR4104: procedures[\"dbo.p\"].paths[\"[]\"]: dbo.p returns 2 result sets, so the path must start with the one it means, as results[0].", run.Error);
    }

    [Fact]
    public void A_result_set_that_does_not_exist_is_an_error()
    {
        var run = Apply("CREATE PROCEDURE dbo.p AS SELECT 1 AS a;\nGO\n", """{ "procedures": { "dbo.p": { "paths": { "results[1][]": { "name": "X" } } } } }""");

        Assert.Equal("haruspex.json:1:41: error HAR4104: procedures[\"dbo.p\"].paths[\"results[1][]\"]: dbo.p returns 1 result set, so there is no results[1].", run.Error);
    }

    [Fact]
    public void A_recursive_function_s_type_is_named_after_the_function()
    {
        const string Tree = """
            CREATE TABLE dbo.node (id int NOT NULL PRIMARY KEY, parent_id int NULL);
            GO
            CREATE FUNCTION dbo.tree(@id int) RETURNS nvarchar(max) AS
            BEGIN
                RETURN (SELECT n.id, json_query(dbo.tree(n.id)) AS children FROM dbo.node AS n WHERE n.parent_id = @id FOR JSON PATH);
            END
            GO
            CREATE PROCEDURE dbo.p AS SELECT n.id, json_query(dbo.tree(n.id)) AS children FROM dbo.node AS n FOR JSON PATH;
            GO
            """;

        Assert.Contains("TreeNode", Apply(Tree, "{}").Document.Types.Keys);

        // The function's schema's settings name it.
        Assert.Contains("DboTreeNode", Apply(Tree, """{ "schemas": { "dbo": { "prefix": "Dbo" } } }""").Document.Types.Keys);

        var run = Apply(Tree, """{ "procedures": { "dbo.p": { "paths": { "[].children[]": { "name": "Child" } } } } }""");
        Assert.Equal(
            "haruspex.json:1:41: error HAR4106: procedures[\"dbo.p\"].paths[\"[].children[]\"]: the value at results[0][].children[] is the recursive type of dbo.tree, which every procedure that reaches it shares, so it can't be overridden for one of them.",
            run.Error);
    }
}
