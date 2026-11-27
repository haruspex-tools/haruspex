using System.Text.Json;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Semantics;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// What a call of <c>sys.sp_invoke_external_rest_endpoint</c> writes into <c>@response</c>, as SQL Server 2025 was observed to write
/// it (RestEndpointObservationTests): the envelope around the service's reply, in JSON or XML as <c>@headers</c> asks. The rules
/// are in docs/decisions/0140-a-rest-response-is-typed-from-a-schema-declared-for-its-endpoint.md; <c>Walker.Rest.cs</c> applies
/// them at a call.
/// </summary>
internal static class RestCalls
{
    /// <summary>The procedure's name, which a call names in any schema (<see cref="SystemProcedures.Find"/>).</summary>
    public const string Procedure = "sp_invoke_external_rest_endpoint";

    /// <summary>How long <c>@url</c> and <c>@headers</c> are, in characters: a longer argument is cut short to it.</summary>
    public const int ArgumentLength = 4000;

    /// <summary>How long <c>@method</c> is, in characters.</summary>
    public const int MethodLength = 6;

    /// <summary>What <c>@response</c> holds, as the <c>Accept</c> header of <c>@headers</c> chooses.</summary>
    internal enum Format
    {
        /// <summary>JSON, its <c>result</c> the body parsed: an object or an array (any other body fails the call, 11558).</summary>
        Json,

        /// <summary>JSON, its <c>result</c> the body as a string.</summary>
        Text,

        /// <summary>XML.</summary>
        Xml,
    }

    /// <summary>Whether <paramref name="procedure"/> is this one.</summary>
    public static bool Is(SystemProcedure procedure) => procedure.Name.Name.Equals(Procedure, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// What a call's <c>@headers</c> asks for, given every text it may be (null when it is left out or NULL): the format, or null
    /// when it is not known, with why. An <c>Accept</c> key, of any case, chooses by its value, of any case: <c>application/json</c>
    /// JSON, <c>application/xml</c> XML, <c>text/plain</c>, <c>text/xml</c>, <c>text/html</c> or <c>text/csv</c> the body as a
    /// string (observed: Accept_decides_whether_the_response_is_json_and_whether_result_is_the_body_parsed_or_a_string). No
    /// <c>Accept</c> is JSON; two are not read. Any other value either fails the call (31638) or was not observed. Text that isn't
    /// a JSON object fails the call (11558), so it describes no response.
    /// </summary>
    public static (Format? Format, string Why) Accepted(IReadOnlyList<string>? headers)
    {
        if (headers is null)
            return (Format.Json, "");
        Format? chosen = null;
        foreach (var text in headers)
        {
            var format = AcceptedBy(text.Length > ArgumentLength ? text[..ArgumentLength] : text, out var why);
            if (format is null)
                return (null, why);
            if (chosen is not null && chosen != format)
                return (null, "the @headers it may send ask for different formats");
            chosen = format;
        }

        return (chosen ?? Format.Json, "");
    }

    private static Format? AcceptedBy(string headers, out string why)
    {
        why = "";
        try
        {
            using var document = JsonDocument.Parse(headers);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return Format.Json; // Not an object: the call fails, so the format is any.
            var accepts = document.RootElement.EnumerateObject().Where(p => p.Name.Equals("Accept", StringComparison.OrdinalIgnoreCase)).ToList();
            if (accepts.Count == 0)
                return Format.Json;
            if (accepts.Count > 1 || accepts[0].Value.ValueKind != JsonValueKind.String)
            {
                why = "its @headers give Accept in a form whose format was not observed";
                return null;
            }

            switch ((accepts[0].Value.GetString() ?? "").ToUpperInvariant())
            {
                case "APPLICATION/JSON":
                    return Format.Json;
                case "APPLICATION/XML":
                    return Format.Xml;
                case "TEXT/PLAIN" or "TEXT/XML" or "TEXT/HTML" or "TEXT/CSV":
                    return Format.Text;
                default:
                    why = $"its @headers accept {accepts[0].Value.GetString()}, whose format was not observed";
                    return null;
            }
        }
        catch (JsonException)
        {
            return Format.Json; // Not JSON: the call fails (11558), so the format is any.
        }
    }

    /// <summary>
    /// The envelope <c>@response</c> holds as JSON: <c>{"response":{"status":{"http":{"code","description"}},"headers"},"result"}</c>
    /// (observed: Response_holds_the_status_the_headers_and_the_body_as_result). <c>code</c> is an integer and <c>description</c> a
    /// string, perhaps empty; <c>headers</c> an object whose keys are data and whose values are strings, perhaps none, perhaps a
    /// key twice (Headers_may_be_none_or_a_key_twice_and_a_status_s_description_may_be_empty); <c>result</c> the body, left out when
    /// it is empty (Result_is_left_out_when_the_body_is_empty), whatever the status.
    /// </summary>
    /// <param name="result">What <c>result</c> holds when it is there.</param>
    /// <param name="at">The call.</param>
    public static Shape Envelope(Shape result, SourceLocation at)
    {
        static FieldShape Field(string name, Shape type) => new(name, type, IrPresence.Always, []);

        // The server writes the status as an int, in a few characters, so JSON_VALUE always gives its text.
        var http = new ObjectShape([Field("code", SqlTypes.ToConvertedShape(new ScalarType(BuiltInType.Int), at)), Field("description", AnyString)]);
        var response = new ObjectShape([Field("status", new ObjectShape([Field("http", http)])), Field("headers", new RecordShape(AnyString, []))]);
        return new ObjectShape([Field("response", response), new FieldShape("result", result, IrPresence.Optional, []) { LeftOutBecause = "as it is when the reply has no body" }]);
    }

    /// <summary>A JSON string of any content.</summary>
    public static ScalarShape AnyString { get; } = new(new IrScalar(IrScalarKind.String, null, null), null);
}
