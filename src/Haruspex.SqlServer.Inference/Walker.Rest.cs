using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using static Haruspex.SqlServer.Inference.Typer;

namespace Haruspex.SqlServer.Inference;

// sys.sp_invoke_external_rest_endpoint's @response: the envelope SQL Server writes around a service's reply (RestCalls), and the
// reply's own type, which only a declaration in the config file can give. README.md in this folder, "Calls to REST endpoints",
// explains it; docs/decisions/0140-a-rest-response-is-typed-from-a-schema-declared-for-its-endpoint.md says why.
internal sealed partial class Walker
{
    /// <summary>
    /// What a call of the REST procedure that returns sets <paramref name="variable"/>, passed as its <c>@response</c>, to, and
    /// whether it surely sets it. A response of any status is written there (observed:
    /// Response_holds_the_status_the_headers_and_the_body_as_result), so it is never NULL where it is set; a call that fails
    /// raises an error and leaves it as it was. One failure lets the caller go on: <c>@headers</c> text that isn't JSON raises
    /// 11558 and returns 1 before anything is sent, leaving <c>@response</c> as it was (observed:
    /// Headers_that_are_not_json_return_1_and_the_caller_goes_on_with_response_as_it_was), so the call surely sets it only where
    /// every text <c>@headers</c> may be is JSON.
    /// </summary>
    /// <remarks>
    /// The variable stays a string of its declared type wherever it is read as one: no request header proves the reply is JSON
    /// (on another build a service's XML answer gives the XML form whatever the request accepted). Where the call accepts JSON
    /// and the variable holds the whole text, it holds the envelope as JSON text (<see cref="JsonPiece"/>), which only a JSON
    /// function reads; each of them raises 13609 on the XML form (observed:
    /// Every_json_function_over_the_xml_form_is_an_error), so what it claims holds of every call that returns a value.
    /// </remarks>
    private (Value Value, bool Surely) RestResponse(ExecuteSpecification execute, SystemProcedure procedure, LocalName variable, FlowState state)
    {
        var at = At(execute);
        var declared = variableShapes.TryGetValue(variable, out var shape) ? shape : new NeverShape(at);
        var arguments = procedure.Arguments(execute);
        var scope = Reading(state);

        var headers = arguments.GetValueOrDefault("@headers")?.ParameterValue;
        (RestCalls.Format? Format, string Why) accepted;
        bool surely;
        if (headers is null || Strip(headers) is NullLiteral)
        {
            (accepted, surely) = (RestCalls.Accepted(null), true);
        }
        else if (Texts(headers, scope, out var unread) is { } sent)
        {
            (accepted, surely) = (RestCalls.Accepted(sent), sent.All(RestCalls.IsJson));
        }
        else
        {
            (accepted, surely) = ((null, unread), false);
        }

        // A variable shorter than max holds the text cut short, without an error (observed:
        // A_variable_shorter_than_max_holds_the_response_cut_short_without_an_error). Assign drops the JSON text's pieces on such
        // a variable anyway, which keeps only nvarchar(max)'s; this says so here, where the reason is the response's, and names it.
        if (declared is not ScalarShape { BuiltIn: { IsMax: true } type } || type.Type != BuiltInType.NVarChar)
            return (NotJson($"{variable} is not nvarchar(max), so the response is cut short when it is longer"), surely);

        var (format, why) = accepted;
        return (format switch
        {
            null => NotJson($"whether the response is JSON or XML is not known: {why}"),
            RestCalls.Format.Xml => NotJson("its @headers accept application/xml, so the response is XML"),
            RestCalls.Format.Text => Json(RestCalls.AnyString),
            _ => Json(Response(execute, arguments, scope, at)),
        }, surely);

        Value Json(Shape result) =>
            Value.Computed(declared, []) with { Pieces = [new JsonPiece(RestCalls.Envelope(result, at))] };

        Value NotJson(string reason) =>
            Value.Computed(declared, []) with { JsonWhy = Unsupported(IrUnknownReason.JsonShapeUnknown, $"{variable} holds the response of {Text(execute)}, which is not read as JSON: {reason}.", at) };
    }

    /// <summary>
    /// What the envelope's <c>result</c> holds when the call accepts JSON: the response declared for the one endpoint every URL and
    /// method it may send matches, or an unknown saying why there is none (<see cref="DeclaredEndpoints"/>). <c>@url</c> is read
    /// from the text the SQL builds, numbers in it too; <c>@method</c> must be literal text, and a call without one sends
    /// <c>POST</c> (observed: A_call_without_a_method_sends_post_and_a_method_is_read_without_case). A NULL of either fails the
    /// call (11554), so it sends nothing to read.
    /// </summary>
    private Shape Response(ExecuteSpecification execute, IReadOnlyDictionary<string, ExecuteParameter> arguments, ReadScope scope, SourceLocation at)
    {
        UnknownShape Unknown(string why) =>
            Unsupported(IrUnknownReason.JsonShapeUnknown, $"The body of the reply to {Text(execute)} is the service's, and is not known: {why}.", at);

        if (arguments.GetValueOrDefault("@url")?.ParameterValue is not { } url)
            return Unknown("it passes no @url");
        if (SentText.Expand(typer.TextOf(url, scope), out var unread) is not { } urls)
            return Unknown($"its @url is built at run time: {unread}");
        if (urls.Any(u => u.MostLength > RestCalls.ArgumentLength))
            return Unknown($"its @url may be longer than the {RestCalls.ArgumentLength:N0} characters @url holds, which cuts it short");

        IReadOnlyList<string> methods = ["POST"];
        if (arguments.GetValueOrDefault("@method")?.ParameterValue is { } method)
        {
            if (Texts(method, scope, out unread, ifNull: null) is not { } sent)
                return Unknown($"its @method is built at run time: {unread}");
            methods = [.. sent.Select(m => (m.Length > RestCalls.MethodLength ? m[..RestCalls.MethodLength] : m).ToUpperInvariant()).Distinct()];
        }

        return summaries.DeclaredEndpoints.Result(urls, methods, new DeclarationReads.Walk(source, context.Arguments), Text(execute), at);
    }

    /// <summary>
    /// The text a call passes each string parameter of <paramref name="callee"/>, where it is known and fits (<see cref="Kept"/>),
    /// when the callee calls a REST endpoint, itself or through others, whose URL, method or headers may be built from it
    /// (<see cref="CallContext.Arguments"/>); none otherwise.
    /// </summary>
    private EquatableMap<LocalName, CallContext.Passed> Passed(ExecuteSpecification execute, Procedure callee, FlowState state)
    {
        if (execute.ExecutableEntity is not ExecutableProcedureReference call || !summaries.CallsRest(callee.Name))
            return [];
        var scope = Reading(state);
        var matched = ProcedureArguments.Matched(call, callee);

        // A parameter the call leaves out takes its default, a constant, which a NULL default leaves NULL.
        var defaults = callee.Parameters
            .Where(p => matched.All(m => m.Parameter != p))
            .SelectMany(p => p.Default is { } value && Strip(value) is not NullLiteral ? [(Value: value, Parameter: p)] : Array.Empty<(ScalarExpression Value, Parameter Parameter)>());
        var passed = new Dictionary<LocalName, CallContext.Passed>();
        foreach (var (argument, parameter) in matched.Concat(defaults))
        {
            var value = typer.Expression(argument, scope) with { Pieces = typer.TextOf(argument, scope) };
            if (!parameter.IsOutput && Kept(value, typer.DeclaredShape(parameter.Type, At(execute), new LocalName(parameter.Name), out _)) is { } pieces)
                passed[new LocalName(parameter.Name)] = new CallContext.Passed(pieces, value.NullableBecause.Count > 0);
        }

        return passed.ToEquatableMap(p => p.Key, p => p.Value);
    }

    /// <summary>
    /// Every text an argument may send, and <paramref name="ifNull"/> where it may be NULL (none when that is null); null when
    /// it is not known, saying why.
    /// </summary>
    private IReadOnlyList<string>? Texts(ScalarExpression argument, ReadScope scope, out string why, string? ifNull = "{}")
    {
        if (SentText.Expand(typer.TextOf(argument, scope), out why) is not { } texts)
            return null;
        if (texts.Any(t => t.Text is null))
        {
            why = $"{Text(argument)} is built from a number";
            return null;
        }

        return [.. texts.Select(t => t.Text).OfType<string>(), .. ifNull is not null && typer.Expression(argument, scope).NullableBecause.Count > 0 ? [ifNull] : Array.Empty<string>()];
    }
}
