using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

/// <summary>
/// A call of <c>sys.sp_invoke_external_rest_endpoint</c>: the envelope it writes into <c>@response</c>, as JSON text whose
/// <c>result</c> is the service's, and when <c>@headers</c> or the variable keep it from being read as JSON. What SQL Server does is
/// observed in <c>RestEndpointObservationTests</c>; why, in decision 0140.
/// </summary>
public partial class RestEndpointTests
{
    private static IrDocument Infer(params (string Name, string Sql)[] procedures) => InferProcedure.Infer("dbo.p", [.. procedures.Select(p => ($"{p.Name}.sql", p.Sql))]);

    /// <summary>A procedure that calls the endpoint with <paramref name="arguments"/> into <c>@response</c>, then runs <paramref name="then"/>.</summary>
    private static IrDocument Call(string arguments, string then, string type = "nvarchar(max)", string chosen = "N'{}'") => Infer(("p", $"""
        CREATE PROCEDURE dbo.p @plan_id int, @path nvarchar(100), @headers nvarchar(4000) AS
        DECLARE @response {type}, @chosen nvarchar(100) = {chosen};
        EXEC sys.sp_invoke_external_rest_endpoint {arguments}, @response = @response OUTPUT;
        {then}
        """));

    private const string Plans = "@url = N'https://billing.example/plans', @method = N'GET'";

    [Fact]
    public void Response_read_plainly_is_its_declared_string_never_null()
    {
        // No request header proves the reply is JSON: another build answers XML whatever was accepted, so the text is a string.
        var doc = Call(Plans, "SELECT @response AS response;");

        Assert.Equal(IrScalarKind.String, Assert.IsType<IrScalar>(Field(doc, "response").Type).Kind);
        Assert.False(Field(doc, "response").Nullable);
    }

    [Fact]
    public void A_json_function_reads_the_envelope_whose_result_may_be_left_out()
    {
        // Each JSON function raises 13609 on the XML form, so what it reads holds wherever it returns.
        var doc = Call(Plans, "SELECT JSON_QUERY(@response) AS envelope;");

        var envelope = Assert.IsType<IrJsonText>(Field(doc, "envelope").Type).Content!;
        Assert.Equal(["response", "result"], Fields(doc, envelope).Select(f => f.Name));
        Assert.Equal(IrPresence.Optional, Fields(doc, envelope)[1].Presence);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData(", @headers = NULL", false)]
    [InlineData(", @headers = N'{}'", false)]
    [InlineData(", @headers = N'[1]'", false)]
    [InlineData(", @headers = N'not json'", true)]
    [InlineData(", @headers = N''", true)]
    [InlineData(", @headers = @headers", true)]
    [InlineData(", @headers = @chosen", true, "JSON_OBJECT('Accept':'application/json')")]
    public void Headers_that_may_not_be_json_may_leave_response_as_it_was(string headers, bool nullable, string chosen = "N'{}'")
    {
        // ISJSON(@headers) = 0 returns 1 before the call, and the caller goes on; JSON that isn't an object fails the call (31620).
        var doc = Call(Plans + headers, "SELECT @response AS response;", chosen: chosen);

        Assert.Equal(nullable, Field(doc, "response").Nullable);
    }

    [Fact]
    public void A_header_built_by_json_object_is_not_read()
    {
        // json_object writes / as \/, and the procedure sends a header's value as that text, so its text is not followed.
        var doc = Call(Plans + ", @headers = @chosen", "SELECT JSON_QUERY(@response, '$.response') AS response;", chosen: "JSON_OBJECT('Accept':'application/json')");

        Assert.Contains("whether the response is JSON or XML is not known", Assert.Single(doc.Unknowns).Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void The_status_code_is_always_there()
    {
        var doc = Call(Plans, """
            SELECT JSON_VALUE(@response, '$.response.status.http.code') AS code,
                   JSON_VALUE(@response, '$.response.headers."Content-Type"') AS content_type,
                   JSON_VALUE(@response, '$.result.name') AS name;
            """);

        Assert.Empty(Rules(doc, "code"));
        Assert.True(Field(doc, "content_type").Nullable);
        Assert.True(Field(doc, "name").Nullable);
    }

    [Fact]
    public void Result_is_unknown_saying_no_endpoint_is_declared_for_it()
    {
        var doc = Call(Plans, "SELECT JSON_QUERY(@response, '$.result') AS result;");

        var unknown = Assert.Single(doc.Unknowns);
        Assert.Equal(IrUnknownReason.JsonShapeUnknown, unknown.Reason);
        Assert.Contains("no endpoint is declared", unknown.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(", @headers = N'{\"Accept\":\"text/plain\"}'")]
    [InlineData(", @headers = N'{\"accept\":\"TEXT/CSV\"}'")]
    public void Accepting_text_makes_result_a_string(string headers)
    {
        var doc = Call(Plans + headers, "SELECT JSON_QUERY(@response, '$.result') AS result, JSON_VALUE(@response, '$.result') AS body;");

        Assert.Contains("never finds an object or array", Assert.Single(doc.Unknowns).Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData(", @headers = NULL", true)]
    [InlineData(", @headers = N'{\"X-Trace\":\"1\"}'", true)]
    [InlineData(", @headers = N'{\"ACCEPT\":\"Application/Json\"}'", true)]
    [InlineData(", @headers = @chosen", true, "N'{\"Accept\":' + N'\"application/json\"}'")]
    [InlineData(", @headers = N'{\"Accept\":\"application/xml\"}'", false)]
    [InlineData(", @headers = N'{\"Accept\":\"*/*\"}'", false)]
    [InlineData(", @headers = N'{\"Accept\":\"application/json\",\"accept\":\"application/json\"}'", false)]
    [InlineData(", @headers = @headers", false)]
    [InlineData(", @headers = @chosen", false, "N'{\"Accept\":' + N'\"application/xml\"}'")]
    [InlineData(", @headers = @chosen", false, "N'{\"Accept\":\"' + @path + N'\"}'")]
    public void Headers_decide_whether_the_response_is_read_as_json(string headers, bool json, string other = "N'{}'")
    {
        // An argument of EXEC is a literal or a variable, so text built for it is built in a variable first.
        var doc = Call(Plans + headers, "SELECT JSON_QUERY(@response, '$.response') AS response;", chosen: other);

        if (json)
        {
            Assert.Empty(doc.Unknowns);
            return;
        }

        var unknown = Assert.Single(doc.Unknowns);
        Assert.Contains("@response holds the response of", unknown.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Arguments_by_position_are_read_in_the_catalogue_s_order()
    {
        // @url, @payload, @headers, @method, @timeout, @credential, @response.
        var doc = Infer(("p", """
            CREATE PROCEDURE dbo.p AS
            DECLARE @response nvarchar(max);
            EXEC sys.sp_invoke_external_rest_endpoint N'https://billing.example/plans', NULL, N'{"Accept":"application/xml"}', N'GET', 30, NULL, @response OUTPUT;
            SELECT JSON_QUERY(@response, '$.response') AS response;
            """));

        Assert.Contains("application/xml", Assert.Single(doc.Unknowns).Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("nvarchar(4000)")]
    [InlineData("varchar(max)")]
    public void A_variable_that_isn_t_nvarchar_max_is_not_read_as_json(string type)
    {
        var doc = Call(Plans, "SELECT JSON_QUERY(@response, '$.response') AS response, @response AS text;", type);

        Assert.Single(doc.Unknowns);
        Assert.False(Field(doc, "text").Nullable);
    }

    [Fact]
    public void A_call_that_fails_leaves_response_as_it_was()
    {
        // The CATCH is reached with @response unassigned: NULL.
        var doc = Infer(("p", $"""
            CREATE PROCEDURE dbo.p AS
            DECLARE @response nvarchar(max);
            BEGIN TRY
                EXEC sys.sp_invoke_external_rest_endpoint {Plans}, @response = @response OUTPUT;
            END TRY
            BEGIN CATCH
            END CATCH
            SELECT JSON_VALUE(@response, '$.response.status.http.code') AS code, @response AS response;
            """));

        Assert.True(Field(doc, "code").Nullable);
        Assert.True(Field(doc, "response").Nullable);
    }
}
