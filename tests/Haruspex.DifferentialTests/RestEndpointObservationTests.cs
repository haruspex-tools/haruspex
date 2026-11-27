using Haruspex.DifferentialTests.Server;

namespace Haruspex.DifferentialTests;

/// <summary>
/// What <c>sys.sp_invoke_external_rest_endpoint</c> does, observed on a server that may call a REST endpoint and the endpoint it
/// calls (<see cref="RestServer"/>): its result sets, its return code, the envelope it writes into <c>@response</c>, and what each
/// of <c>@url</c>, <c>@method</c> and <c>@headers</c> changes. The rules are applied in <c>SystemProcedures</c>, <c>RestCalls</c>
/// and <c>RestEndpoints</c>, and tested in <c>RestEndpointTests</c>; why, in decision 0140. Observed on SQL Server 2025 RTM-CU10.
/// </summary>
public class RestEndpointObservationTests
{
    private const string Endpoint = RestServer.Endpoint;
    private const string Raw = RestServer.RawEndpoint;

    /// <summary>
    /// A call with <paramref name="arguments"/> (besides <c>@response</c>) whose error, if it raises one, is a row of its own, then
    /// one row: the return code and <paramref name="read"/> of <c>@response</c>. Both start as values no call gives, so a call
    /// that leaves them is seen to.
    /// </summary>
    private static string Call(string arguments, string read = "@r", string type = "nvarchar(max)") => $"""
        SET NOCOUNT ON;
        DECLARE @r {type} = N'before', @rc int = -1;
        BEGIN TRY
            EXEC @rc = sys.sp_invoke_external_rest_endpoint {arguments}, @response = @r OUTPUT;
        END TRY
        BEGIN CATCH
            SELECT ERROR_NUMBER() AS error;
        END CATCH
        SELECT @rc AS rc, {read} AS r;
        """;

    [Fact]
    public async Task It_returns_no_result_set_and_its_return_code_is_0_for_a_2xx_status_and_the_status_otherwise() =>
        // Only the batch's own SELECT arrives. A redirect is not followed: its status is the return code.
        Assert.Equal(
            "a,b,c,d,e,f: 0,0,302,404,500,599",
            (await RestServer.RunAsync($"""
                SET NOCOUNT ON;
                DECLARE @r nvarchar(max), @a int, @b int, @c int, @d int, @e int, @f int;
                EXEC @a = sys.sp_invoke_external_rest_endpoint @url = N'{Endpoint}/plans/1', @method = N'GET', @response = @r OUTPUT;
                EXEC @b = sys.sp_invoke_external_rest_endpoint @url = N'{Endpoint}/status/201', @method = N'GET', @response = @r OUTPUT;
                EXEC @c = sys.sp_invoke_external_rest_endpoint @url = N'{Endpoint}/status/302', @method = N'GET', @response = @r OUTPUT;
                EXEC @d = sys.sp_invoke_external_rest_endpoint @url = N'{Endpoint}/status/404', @method = N'GET', @response = @r OUTPUT;
                EXEC @e = sys.sp_invoke_external_rest_endpoint @url = N'{Endpoint}/status/500', @method = N'GET';
                EXEC @f = sys.sp_invoke_external_rest_endpoint @url = N'{Raw}/twice', @method = N'GET', @response = @r OUTPUT;
                SELECT @a AS a, @b AS b, @c AS c, @d AS d, @e AS e, @f AS f;
                """)).ToString());

    /// <summary>
    /// Every key of the envelope with its OPENJSON type (1 a string, 2 a number, 4 an array, 5 an object), level by level, and the
    /// types of the headers' values.
    /// </summary>
    private const string Envelope = """
        (SELECT STRING_AGG(CONCAT(x.level, '.', x.[key], ':', x.type), ' ') WITHIN GROUP (ORDER BY x.level, x.[key]) FROM (
            SELECT '1' AS level, [key], type FROM OPENJSON(@r)
            UNION ALL SELECT '2', [key], type FROM OPENJSON(@r, '$.response')
            UNION ALL SELECT '3', [key], type FROM OPENJSON(@r, '$.response.status')
            UNION ALL SELECT '4', [key], type FROM OPENJSON(@r, '$.response.status.http')
            UNION ALL SELECT DISTINCT '5', 'header', type FROM OPENJSON(@r, '$.response.headers')) AS x)
        """;

    [Theory]
    [InlineData("/plans/1", "0", "1.response:5 1.result:5 2.headers:5 2.status:5 3.http:5 4.code:2 4.description:1 5.header:1")]
    [InlineData("/array", "0", "1.response:5 1.result:4 2.headers:5 2.status:5 3.http:5 4.code:2 4.description:1 5.header:1")]
    [InlineData("/status/404", "404", "1.response:5 1.result:5 2.headers:5 2.status:5 3.http:5 4.code:2 4.description:1 5.header:1")]
    public async Task Response_holds_the_status_the_headers_and_the_body_as_result(string path, string code, string keys) =>
        // A response of any status is written into @response, the body of an error too.
        Assert.Equal($"rc,r: {code},{keys}", (await RestServer.RunAsync(Call($"@url = N'{Endpoint}{path}', @method = N'GET'", Envelope))).ToString());

    [Theory]
    [InlineData("/empty", "GET", "204")]
    [InlineData("/empty200", "GET", "200")]
    [InlineData("/plans/1", "HEAD", "200")]
    public async Task Result_is_left_out_when_the_body_is_empty(string path, string method, string status) =>
        Assert.Equal(
            $"rc,r: 0,{status} response",
            (await RestServer.RunAsync(Call(
                $"@url = N'{Endpoint}{path}', @method = N'{method}'",
                "CONCAT(JSON_VALUE(@r, '$.response.status.http.code'), ' ', (SELECT STRING_AGG([key], ',') FROM OPENJSON(@r)))"))).ToString());

    [Fact]
    public async Task Headers_may_be_none_or_a_key_twice_and_a_status_s_description_may_be_empty() =>
        // A header sent twice is written twice, as one key twice in one object; JSON_VALUE reads the first.
        Assert.Equal(
            "bare,reason,twice: {},,{\"X-Count\":\"5\",\"X-Count\":\"6\"}",
            (await RestServer.RunAsync($"""
                SET NOCOUNT ON;
                DECLARE @a nvarchar(max), @b nvarchar(max), @c nvarchar(max);
                EXEC sys.sp_invoke_external_rest_endpoint @url = N'{Raw}/bare', @method = N'GET', @response = @a OUTPUT;
                EXEC sys.sp_invoke_external_rest_endpoint @url = N'{Raw}/no-reason', @method = N'GET', @response = @b OUTPUT;
                EXEC sys.sp_invoke_external_rest_endpoint @url = N'{Raw}/twice', @method = N'GET', @response = @c OUTPUT;
                SELECT JSON_QUERY(@a, '$.response.headers') AS bare, JSON_VALUE(@b, '$.response.status.http.description') AS reason,
                       JSON_QUERY(@c, '$.response.headers') AS twice;
                """)).ToString());

    [Theory]
    [InlineData(Endpoint + "/text")]
    [InlineData(Endpoint + "/xml")]
    [InlineData(Endpoint + "/null")]
    [InlineData(Endpoint + "/number")]
    [InlineData(Endpoint + "/broken")]
    [InlineData(Raw + "/string")]
    [InlineData(Raw + "/true")]
    public async Task Under_json_a_body_that_is_not_an_object_or_an_array_fails_the_call_and_leaves_response_as_it_was(string address) =>
        Assert.Equal("error: 11558 | rc,r: -1,before", (await RestServer.RunAsync(Call($"@url = N'{address}', @method = N'GET'"))).ToString());

    [Theory]
    [InlineData("@url = N'https://nowhere.invalid/plans'", 31608)]
    [InlineData("@url = N'http://rest.haruspex.test/plans'", 31610)]
    [InlineData("@url = N'" + Endpoint + "/plans', @method = N'FETCH'", 31605)]
    [InlineData("@url = N'" + Endpoint + "/plans', @method = N'GET '", 31605)]
    [InlineData("@url = N'" + Endpoint + "/plans', @method = NULL", 11554)]
    [InlineData("@url = NULL", 11554)]
    [InlineData("@url = N'" + Endpoint + "/slow', @method = N'GET', @timeout = 1", 31629)]
    [InlineData("@url = N'" + Endpoint + "/plans', @headers = N'{\"Accept\":\"*/*\"}'", 31638)]
    [InlineData("@url = N'" + Endpoint + "/plans', @headers = N'not json'", 11558)]
    public async Task A_call_that_fails_raises_an_error_and_leaves_the_return_code_and_response_as_they_were(string arguments, int error) =>
        Assert.Equal($"error: {error} | rc,r: -1,before", (await RestServer.RunAsync(Call(arguments))).ToString());

    [Theory]
    [InlineData("", "5")]
    [InlineData(", @headers = NULL", "5")]
    [InlineData(", @headers = N'{}'", "5")]
    [InlineData(", @headers = N'{\"X-Other\":\"1\"}'", "5")]
    [InlineData(", @headers = N'{\"Accept\":\"application/json\"}'", "5")]
    [InlineData(", @headers = N'{\"ACCEPT\":\"APPLICATION/JSON\"}'", "5")]
    [InlineData(", @headers = N'{\"Accept\":\"text/plain\"}'", "1")]
    [InlineData(", @headers = N'{\"accept\":\"text/xml\"}'", "1")]
    [InlineData(", @headers = N'{\"Accept\":\"text/html\"}'", "1")]
    [InlineData(", @headers = N'{\"Accept\":\"text/csv\"}'", "1")]
    [InlineData(", @headers = N'{\"Accept\":\"application/xml\"}'", "xml")]
    [InlineData(", @headers = N'{\"accept\":\"Application/Xml\"}'", "xml")]
    [InlineData(", @headers = N'{\"Accept\":\"application/json\",\"accept\":\"application/xml\"}'", "xml")]
    public async Task Accept_decides_whether_the_response_is_json_and_whether_result_is_the_body_parsed_or_a_string(string headers, string result) =>
        // Under JSON, result's OPENJSON type: 5 an object (the body parsed), 1 a string (the body as text). Otherwise XML.
        Assert.Equal(
            $"rc,r: 0,{result}",
            (await RestServer.RunAsync(Call(
                $"@url = N'{Endpoint}/plans/1', @method = N'GET'{headers}",
                "IIF(ISJSON(@r) = 1, (SELECT CAST(type AS varchar(3)) FROM OPENJSON(@r) WHERE [key] = 'result'), IIF(@r LIKE N'<output>%', 'xml', @r))"))).ToString());

    [Theory]
    [InlineData("", "POST")]
    [InlineData(", @method = N'get'", "GET")]
    [InlineData(", @method = N'Delete'", "DELETE")]
    public async Task A_call_without_a_method_sends_post_and_a_method_is_read_without_case(string method, string sent) =>
        Assert.Equal($"rc,r: 0,{sent}", (await RestServer.RunAsync(Call($"@url = N'{Endpoint}/echo'{method}", "JSON_VALUE(@r, '$.result.method')"))).ToString());

    [Theory]
    [InlineData("/echo?b=2&a=1", "/echo?b=2&a=1")]
    [InlineData("/echo#part", "/echo%23part")]
    [InlineData("/Echo", "/Echo")]
    public async Task The_path_the_query_and_a_fragment_are_sent_as_written(string path, string target) =>
        // A fragment is sent to the service as part of the path, encoded; the path keeps its case.
        Assert.Equal($"rc,r: 0,{target}", (await RestServer.RunAsync(Call($"@url = N'{Endpoint}{path}', @method = N'GET'", "JSON_VALUE(@r, '$.result.target')"))).ToString());

    [Fact]
    public async Task The_scheme_and_the_host_are_read_without_case() =>
        Assert.Equal("rc,r: 0,/plans/1", (await RestServer.RunAsync(Call("@url = N'HTTPS://REST.HARUSPEX.TEST/plans/1', @method = N'GET'", "JSON_VALUE(@r, '$.result.target')"))).ToString());

    [Fact]
    public async Task Arguments_by_position_are_in_the_order_sys_all_parameters_lists() =>
        // @url, @payload, @headers, @method, @timeout, @credential, @response (SystemProcedures lists them so).
        Assert.Equal(
            "rc,r: 0,GET",
            (await RestServer.RunAsync($"""
                SET NOCOUNT ON;
                DECLARE @r nvarchar(max), @rc int;
                EXEC @rc = sys.sp_invoke_external_rest_endpoint N'{Endpoint}/echo', NULL, NULL, N'GET', 30, NULL, @r OUTPUT;
                SELECT @rc AS rc, JSON_VALUE(@r, '$.result.method') AS r;
                """)).ToString());

    [Theory]
    [InlineData("nvarchar(40)", "0")]
    [InlineData("nvarchar(max)", "1")]
    public async Task A_variable_shorter_than_max_holds_the_response_cut_short_without_an_error(string type, string json) =>
        Assert.Equal($"rc,r: 0,{json}", (await RestServer.RunAsync(Call($"@url = N'{Endpoint}/plans/1', @method = N'GET'", "ISJSON(@r)", type))).ToString());

    [Theory]
    [InlineData("N'not json'")]
    [InlineData("N''")]
    [InlineData("N'1'")]
    public async Task Headers_that_are_not_json_return_1_and_the_caller_goes_on_with_response_as_it_was(string headers) =>
        // ISJSON(@headers) = 0 raises 11558 and returns 1 before the procedure's TRY, so outside a TRY the batch goes on. Every
        // other failure ends it.
        Assert.Equal(
            "rc,r: 1,before ~ 11558",
            (await RestServer.RunAsync($"""
                SET NOCOUNT ON;
                DECLARE @r nvarchar(max) = N'before', @rc int = -1;
                EXEC @rc = sys.sp_invoke_external_rest_endpoint @url = N'{Endpoint}/plans/1', @method = N'GET', @headers = {headers}, @response = @r OUTPUT;
                SELECT @rc AS rc, @r AS r;
                """, errorsAsMessages: true)).ToString());

    [Theory]
    [InlineData("@method = N'GET', @headers = N'[1]'", 31620)]
    [InlineData("@method = N'FETCH'", 31605)]
    [InlineData("@method = N'POST', @payload = N'not json'", 11558)]
    [InlineData("@method = N'GET', @timeout = 0", 31604)]
    public async Task Any_other_failure_ends_the_batch(string arguments, int error) =>
        Assert.Equal(
            $" ! {error}",
            (await RestServer.RunAsync($"""
                SET NOCOUNT ON;
                DECLARE @r nvarchar(max) = N'before', @rc int = -1;
                EXEC @rc = sys.sp_invoke_external_rest_endpoint @url = N'{Endpoint}/plans/1', {arguments}, @response = @r OUTPUT;
                SELECT @rc AS rc, @r AS r;
                """)).ToString());

    [Fact]
    public async Task Every_json_function_over_the_xml_form_is_an_error() =>
        // 13609 from JSON_VALUE, lax or strict, json_query with or without a path, and OPENJSON with or without WITH; ISJSON is 0.
        // Each failing SELECT sends its columns, and no row, before the error.
        Assert.Equal(
            "isj: 0 | v:  | e: 13609 | v:  | e: 13609 | v:  | e: 13609 | v:  | e: 13609 | key:  | e: 13609 | c:  | e: 13609",
            (await RestServer.RunAsync($$"""
                SET NOCOUNT ON;
                DECLARE @r nvarchar(max);
                EXEC sys.sp_invoke_external_rest_endpoint @url = N'{{Endpoint}}/plans/1', @method = N'GET', @headers = N'{"Accept":"application/xml"}', @response = @r OUTPUT;
                SELECT ISJSON(@r) AS isj;
                BEGIN TRY SELECT JSON_VALUE(@r, '$.response.status.http.code') AS v; END TRY BEGIN CATCH SELECT ERROR_NUMBER() AS e; END CATCH
                BEGIN TRY SELECT JSON_VALUE(@r, 'strict $.response') AS v; END TRY BEGIN CATCH SELECT ERROR_NUMBER() AS e; END CATCH
                BEGIN TRY SELECT JSON_QUERY(@r, '$.result') AS v; END TRY BEGIN CATCH SELECT ERROR_NUMBER() AS e; END CATCH
                BEGIN TRY SELECT JSON_QUERY(@r) AS v; END TRY BEGIN CATCH SELECT ERROR_NUMBER() AS e; END CATCH
                BEGIN TRY SELECT [key] FROM OPENJSON(@r); END TRY BEGIN CATCH SELECT ERROR_NUMBER() AS e; END CATCH
                BEGIN TRY SELECT c FROM OPENJSON(@r) WITH (c int '$.response.status.http.code'); END TRY BEGIN CATCH SELECT ERROR_NUMBER() AS e; END CATCH
                """)).ToString());

    [Fact]
    public async Task A_header_is_sent_as_the_text_json_object_writes_and_accept_is_read_decoded() =>
        // json_object writes / as \/ and " as \": the service receives that text. The Accept that chooses the form is decoded, so
        // json_object's application\/xml still asks for XML.
        Assert.Equal(
            "path,quote: \\/a\\/b,say \\\"hi\\\" | form: xml",
            (await RestServer.RunAsync($$"""
                SET NOCOUNT ON;
                DECLARE @r nvarchar(max), @h nvarchar(4000) = JSON_OBJECT('X-Path': '/a/b', 'X-Quote': 'say "hi"');
                EXEC sys.sp_invoke_external_rest_endpoint @url = N'{{Endpoint}}/echo', @method = N'GET', @headers = @h, @response = @r OUTPUT;
                SELECT JSON_VALUE(@r, '$.result.headers."x-path"') AS path, JSON_VALUE(@r, '$.result.headers."x-quote"') AS quote;
                SET @h = JSON_OBJECT('Accept': 'application/xml');
                EXEC sys.sp_invoke_external_rest_endpoint @url = N'{{Endpoint}}/plans/1', @method = N'GET', @headers = @h, @response = @r OUTPUT;
                SELECT IIF(@r LIKE N'<output>%', 'xml', 'json') AS form;
                """)).ToString());

    [Fact]
    public async Task On_this_build_an_xml_answer_to_a_json_request_fails_or_leaves_result_out() =>
        // SQL Server 2025 RTM-CU10: only Accept application/xml gives the XML form. Another build has given it for an XML answer to
        // a JSON request, so no request header is read as proof the reply is JSON (decision 0140).
        Assert.Equal(
            "error: 11558 | rc,keys: 500,response",
            (await RestServer.RunAsync($"""
                SET NOCOUNT ON;
                DECLARE @r nvarchar(max), @rc int;
                BEGIN TRY
                    EXEC @rc = sys.sp_invoke_external_rest_endpoint @url = N'{Endpoint}/xml', @method = N'GET', @response = @r OUTPUT;
                END TRY
                BEGIN CATCH
                    SELECT ERROR_NUMBER() AS error;
                END CATCH
                EXEC @rc = sys.sp_invoke_external_rest_endpoint @url = N'{Endpoint}/xml500', @method = N'GET', @response = @r OUTPUT;
                SELECT @rc AS rc, (SELECT STRING_AGG([key], ',') FROM OPENJSON(@r)) AS keys;
                """)).ToString());
}
