using System.Text;
using Haruspex.Cli;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

// A response declared for an endpoint (endpoints in the config file): which calls take it, through literals, concatenation,
// numbers and a wrapper's parameter, and which stay unknown, saying why; and how the IR records it, as declared.
public partial class RestEndpointTests
{
    private const string Plan = """
        { "type": "object", "required": ["id", "name"], "properties": {
            "id": { "type": "integer", "format": "int32" },
            "name": { "type": "string", "maxLength": 40 },
            "tier": { "enum": ["basic", "plus"] } } }
        """;

    private const string Endpoints = """
        "endpoints": [
            { "url": "https://billing.example/plans/{id}", "method": "GET", "response": "schemas/plan.json" },
            { "url": "https://billing.example/plans", "method": "GET", "response": "schemas/plans.json" },
            { "url": "https://billing.example/plans?tier={tier}", "method": "GET", "response": "schemas/plans.json" } ]
        """;

    /// <summary>Infers every procedure of <paramref name="procedures"/>, with <paramref name="endpoints"/> declared.</summary>
    private static Outcome<IrDocument> Declared(string procedures, string endpoints = Endpoints)
    {
        using var dir = new TempDirectory();
        dir.File("db/procedures.sql", procedures);
        dir.File("schemas/plan.json", Plan);
        dir.File("schemas/plans.json", """{ "type": "array", "items": { "$ref": "plan.json" } }""");
        var project = SqlProject.Load(Path.Combine(dir.Path, "db"));
        Assert.Empty(project.Diagnostics);
        var read = ConfigReader.Read(Encoding.UTF8.GetBytes($"{{ {endpoints} }}"), Path.Combine(dir.Path, "haruspex.json"), dir.Path);
        Assert.DoesNotContain(read.Diagnostics, d => d.IsError);
        return project.Infer(project.Procedures, read.Expected());
    }

    /// <summary>A procedure <c>billing.p</c> that calls with <paramref name="arguments"/>, then reads the plan's name.</summary>
    private static IrDocument Named(string arguments, string declare = "") => Declared($"""
        CREATE SCHEMA billing;
        GO
        CREATE PROCEDURE billing.p @plan_id int, @path nvarchar(100), @tier varchar(5) AS
        DECLARE @response nvarchar(max), @id int = ISNULL(@plan_id, 0){declare};
        EXEC sys.sp_invoke_external_rest_endpoint {arguments}, @response = @response OUTPUT;
        SELECT JSON_VALUE(@response, '$.result.name') AS name, JSON_QUERY(@response, '$.result') AS plan_body;
        """).Expected();

    /// <summary>Whether the call's result was typed from the declaration: its body is known, and the claim is listed.</summary>
    private static void AssertDeclared(IrDocument doc, string endpoint)
    {
        Assert.Empty(doc.Unknowns);
        Assert.Contains(doc.Overrides, o => o is { Setting: IrOverrideSetting.EndpointJson } && o.Via == endpoint);
    }

    private static string Unknown(IrDocument doc) => Assert.Single(doc.Unknowns).Detail;

    [Theory]
    [InlineData("@url = N'https://billing.example/plans/7', @method = N'GET'")]
    [InlineData("@url = N'HTTPS://Billing.Example/plans/7', @method = N'get'")]
    [InlineData("@url = @url, @method = N'GET'", ", @url nvarchar(200) = N'https://billing.example/plans/' + CAST(@id AS nvarchar(11))")]
    [InlineData("@url = @url, @method = N'GET'", ", @url nvarchar(200) = CONCAT(N'https://billing.example/plans/', @id)")]
    [InlineData("@url = @url, @method = N'GET'", ", @url nvarchar(max) = N'https://billing.example/plans/' + CAST(@id AS varchar(11))")]
    public void A_url_every_text_of_which_matches_one_declaration_takes_its_response(string arguments, string declare = "")
    {
        var doc = Named(arguments, declare);

        AssertDeclared(doc, "GET https://billing.example/plans/{id}");
        Assert.True(Field(doc, "name").Nullable); // result may be left out: an empty body, or an error's
        Assert.Equal(IrScalarKind.String, Assert.IsType<IrScalar>(Field(doc, "name").Type).Kind);
    }

    [Fact]
    public void A_response_s_body_reached_through_json_query_has_the_declared_shape()
    {
        var doc = Named("@url = N'https://billing.example/plans/7', @method = N'GET'");

        // Through a driver, live JSON arrives as text holding it.
        Assert.Equal(["id", "name", "tier"], Fields(doc, Assert.IsType<IrJsonText>(Field(doc, "plan_body").Type).Content!).Select(f => f.Name));
    }

    [Theory]
    [InlineData("@url = @url, @method = N'GET'", ", @url nvarchar(200) = N'https://billing.example/plans/' + @path", "@url")]
    [InlineData("@url = @url, @method = N'GET'", ", @url nvarchar(30) = N'https://billing.example/plans/' + CAST(@id AS nvarchar(11))", "@url")]
    [InlineData("@url = @url, @method = N'GET'", ", @url nvarchar(200) = N'https://billing.example/plans/' + CAST(@plan_id AS nvarchar(11))", "GET https://billing.example/plans/, which no endpoint")]
    [InlineData("@url = @url, @method = N'GET'", ", @url nvarchar(200) = CONCAT(N'https://billing.example/plans/', @plan_id)", "GET https://billing.example/plans/, which no endpoint")]
    [InlineData("@url = N'https://billing.example/plans/7', @method = @method", ", @method nvarchar(6) = UPPER(@path)", "@method")]
    [InlineData("@url = N'https://billing.example/plans/7'", "", "no endpoint declared in the config file matches")]
    [InlineData("@url = N'https://billing.example/plans/7/lines', @method = N'GET'", "", "no endpoint declared in the config file matches")]
    [InlineData("@url = N'https://billing.example/plans/', @method = N'GET'", "", "no endpoint declared in the config file matches")]
    [InlineData("@url = N'https://billing.example/plans/7?full=1', @method = N'GET'", "", "no endpoint declared in the config file matches")]
    [InlineData("@url = N'https://billing.example/plans/7#top', @method = N'GET'", "", "no endpoint declared in the config file matches")]
    [InlineData("@url = N'https://billing.example/Plans/7', @method = N'GET'", "", "no endpoint declared in the config file matches")]
    public void A_url_or_method_built_at_run_time_or_matching_no_declaration_leaves_result_unknown(string arguments, string declare, string why)
    {
        // A NULL @plan_id is empty in CONCAT, and in + unless the callers' sessions are declared to set CONCAT_NULL_YIELDS_NULL on,
        // so those calls may send .../plans/ too, which no declaration matches.
        var doc = Named(arguments, declare);

        Assert.Contains(why, Unknown(doc), StringComparison.Ordinal);
        Assert.DoesNotContain(doc.Overrides, o => o.Setting == IrOverrideSetting.EndpointJson);
    }

    [Fact]
    public void A_url_that_matches_two_declarations_leaves_result_unknown()
    {
        // A {name} never holds / or ?, so two templates overlap only where one's literal text is a value the other's {name} stands for.
        const string Overlapping = """
            "endpoints": [
                { "url": "https://billing.example/plans/{id}", "method": "GET", "response": "schemas/plan.json" },
                { "url": "https://billing.example/plans/current", "method": "GET", "response": "schemas/plan.json" } ]
            """;
        var doc = Declared("""
            CREATE PROCEDURE dbo.p AS
            DECLARE @response nvarchar(max);
            EXEC sys.sp_invoke_external_rest_endpoint @url = N'https://billing.example/plans/current', @method = N'GET', @response = @response OUTPUT;
            SELECT JSON_QUERY(@response, '$.result') AS plan_body;
            """, Overlapping).Expected();

        Assert.Contains("GET https://billing.example/plans/{id} and GET https://billing.example/plans/current all match", Unknown(doc), StringComparison.Ordinal);
    }

    [Fact]
    public void A_url_that_may_reach_two_declarations_leaves_result_unknown()
    {
        var doc = Named("@url = @url, @method = N'GET'", ", @url nvarchar(200) = IIF(@plan_id = 1, N'https://billing.example/plans/' + CAST(@id AS nvarchar(11)), N'https://billing.example/plans')");

        Assert.Contains("two endpoints declared in the config file", Unknown(doc), StringComparison.Ordinal);
    }

    [Fact]
    public void A_query_value_is_a_template_s_name_and_literal_alternatives_each_match()
    {
        var doc = Named("@url = @url, @method = N'GET'", ", @url nvarchar(200) = N'https://billing.example/plans?tier=' + IIF(@plan_id = 1, N'basic', N'plus')");

        AssertDeclared(doc, "GET https://billing.example/plans?tier={tier}");
    }

    [Fact]
    public void Accepting_xml_or_text_never_takes_a_declared_response()
    {
        var doc = Named("@url = N'https://billing.example/plans/7', @method = N'GET', @headers = N'{\"Accept\":\"text/plain\"}'");

        Assert.DoesNotContain(doc.Overrides, o => o.Setting == IrOverrideSetting.EndpointJson);
    }

    [Fact]
    public void A_wrapper_s_parameter_is_read_at_each_call_with_the_text_its_caller_passes()
    {
        // billing.call_api builds the URL from @path: typed in each caller from the literal it passes, and unknown in itself,
        // which an application may call with any path.
        var doc = Declared("""
            CREATE SCHEMA billing;
            GO
            CREATE PROCEDURE billing.call_api @path nvarchar(200), @method nvarchar(6) = N'GET' AS
            DECLARE @response nvarchar(max), @url nvarchar(400) = N'https://billing.example' + @path;
            EXEC sys.sp_invoke_external_rest_endpoint @url = @url, @method = @method, @response = @response OUTPUT;
            SELECT JSON_QUERY(@response, '$.result') AS body;
            GO
            CREATE PROCEDURE billing.get_plan @plan_id int AS
            DECLARE @path nvarchar(20) = N'/plans/' + CAST(ISNULL(@plan_id, 0) AS nvarchar(11));
            EXEC billing.call_api @path = @path, @method = N'GET';
            GO
            CREATE PROCEDURE billing.list_plans AS
            EXEC billing.call_api N'/plans';
            GO
            CREATE PROCEDURE billing.anything @path nvarchar(200) AS
            EXEC billing.call_api @path, N'GET';
            """).Expected();

        Assert.Contains(doc.Overrides, o => o is { Object: "billing.get_plan", Setting: IrOverrideSetting.EndpointJson, Via: "GET https://billing.example/plans/{id}" });
        Assert.Contains(doc.Overrides, o => o is { Object: "billing.list_plans", Setting: IrOverrideSetting.EndpointJson, Via: "GET https://billing.example/plans" });
        var listed = Assert.Single(Object(doc, "billing.list_plans").Results ?? []).Type;
        Assert.IsType<IrArray>(Assert.IsType<IrJsonText>(Assert.Single(Fields(doc, listed)).Type).Content);
        Assert.Contains(doc.Unknowns, u => u.Object == "billing.call_api" && u.Detail.Contains("its @url is built at run time", StringComparison.Ordinal));
        Assert.Contains(doc.Unknowns, u => u.Object == "billing.anything" && u.Detail.Contains("its @url is built at run time", StringComparison.Ordinal));
        Assert.DoesNotContain(doc.Unknowns, u => u.Object is "billing.get_plan" or "billing.list_plans");

        // Each object lists only the endpoint its own calls read: neither the wrapper nor a caller passing any text rests on one.
        Assert.DoesNotContain(doc.Overrides, o => o.Object is "billing.call_api" or "billing.anything");
        Assert.Single(doc.Overrides, o => o.Object == "billing.get_plan");
    }

    [Fact]
    public void The_ir_records_each_declaration_with_its_files_hashes()
    {
        var doc = Named("@url = N'https://billing.example/plans/7', @method = N'GET'");

        Assert.Equal(IrDocument.CurrentVersion, doc.Version);
        var plan = Assert.Single(doc.Endpoints, e => e.Address == "https://billing.example/plans/{id}");
        Assert.Equal(("GET", "schemas/plan.json"), (plan.Method, plan.Schema));
        Assert.Equal(["schemas/plan.json"], plan.Files.Keys);
        Assert.Equal(["schemas/plan.json", "schemas/plans.json"], Assert.Single(doc.Endpoints, e => e.Address == "https://billing.example/plans").Files.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_changed_schema_file_is_a_change_check_reports()
    {
        var before = Named("@url = N'https://billing.example/plans/7', @method = N'GET'");
        var after = before with { Endpoints = [.. before.Endpoints.Select(e => e.Address.EndsWith("{id}", StringComparison.Ordinal) ? e with { Files = e.Files.SetItem("schemas/plan.json", new string('0', 64)) } : e)] };

        var change = Assert.Single(IrComparison.Compare(before, after));
        Assert.Equal(IrChangeKind.EndpointDeclarationChanged, change.Kind);
        Assert.Equal("endpoints[\"GET https://billing.example/plans/{id}\"]", change.Path);
        Assert.Contains("schemas/plan.json 000000000000", change.ToString(), StringComparison.Ordinal);
    }
}
