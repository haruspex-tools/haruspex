using Haruspex.Diagnostics;
using Haruspex.Ir;
using static Haruspex.SqlServer.Inference.Typer;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// The responses declared for external REST endpoints, and which one a call reads: the one declaration every URL and method the
/// call may send matches, or none, saying why. Each match is recorded as its reader's (<see cref="DeclarationReads"/>), so that an
/// object's claims list every declaration its results may rest on
/// (docs/decisions/0140-a-rest-response-is-typed-from-a-schema-declared-for-its-endpoint.md).
/// </summary>
internal sealed class DeclaredEndpoints
{
    private readonly List<(UrlTemplate Url, DeclaredEndpoint Declared)> endpoints = [];
    private readonly Dictionary<DeclaredEndpoint, Shape> contents = [];
    private readonly DeclarationReads reads;

    /// <param name="endpoints">The declarations; one whose URL is not a template is never matched (the config layer refuses it).</param>
    /// <param name="reads">Where each match is recorded.</param>
    public DeclaredEndpoints(IReadOnlyList<DeclaredEndpoint> endpoints, DeclarationReads reads)
    {
        this.reads = reads;
        foreach (var declared in endpoints)
        {
            if (UrlTemplate.Parse(declared.Record.Address, out _) is { } url)
                this.endpoints.Add((url, declared));
        }
    }

    /// <summary>
    /// What <c>result</c> holds when a call sends one of <paramref name="urls"/> with one of <paramref name="methods"/> (upper case)
    /// and accepts JSON: the declared response, narrowed to the objects and arrays it may be, since any other body fails the call
    /// (11558); or an unknown saying why no one declaration is certain.
    /// </summary>
    /// <param name="reader">The walk that makes the call.</param>
    /// <param name="call">The call, as the SQL writes it, for a reason.</param>
    /// <param name="at">Where it is.</param>
    public Shape Result(IReadOnlyList<SentText> urls, IReadOnlyList<string> methods, DeclarationReads.Walk reader, string call, SourceLocation at)
    {
        if (endpoints.Count == 0)
            return Unknown("no endpoint is declared in the config file");
        DeclaredEndpoint? found = null;
        foreach (var url in urls)
        {
            foreach (var method in methods)
            {
                var matched = endpoints.Where(e => e.Declared.Record.Method == method && e.Url.Matches(url)).Select(e => e.Declared).ToList();
                switch (matched)
                {
                    case []:
                        return Unknown($"it may send {method} {Shown(url)}, which no endpoint declared in the config file matches");
                    case [var one] when found is null || found == one:
                        found = one;
                        break;
                    case [var one]:
                        return Unknown($"it may send to {found.Record.Endpoint} and to {one.Record.Endpoint}, two endpoints declared in the config file");
                    default:
                        return Unknown($"it may send {method} {Shown(url)}, which {string.Join(" and ", matched.Select(m => m.Record.Endpoint))} all match");
                }
            }
        }

        if (found is null)
            return Unknown("it sends nothing that can be read");
        reads.Read(reader, IrOverrideSetting.EndpointJson, found.Record.Endpoint);
        if (!contents.TryGetValue(found, out var content))
            contents[found] = content = Narrowed(IrShapes.ToShape(found.Content, found.Types, $"The response declared for {found.Record.Endpoint}", at), found.Record.Endpoint, at);
        return content;

        UnknownShape Unknown(string why) =>
            Unsupported(IrUnknownReason.JsonShapeUnknown, $"The body of the reply to {call} is the service's, and is not known: {why}.", at);
    }

    /// <summary>
    /// The objects and arrays among what a declared response may be: under JSON, a body that is anything else fails the call
    /// (observed: Under_json_a_body_that_is_not_an_object_or_an_array_fails_the_call_and_leaves_response_as_it_was).
    /// </summary>
    private static Shape Narrowed(Shape shape, string endpoint, SourceLocation at)
    {
        var kept = Kept(shape);
        return kept switch
        {
            [] => Unsupported(IrUnknownReason.JsonShapeUnknown, $"The response declared for {endpoint} is never an object or an array, which is all a call that accepts JSON receives: any other body fails the call (11558).", at),
            [var one] => one,
            _ => new UnionShape([.. kept]),
        };

        static List<Shape> Kept(Shape shape) => shape switch
        {
            ObjectShape or RecordShape or TupleShape or ArrayShape or UnknownShape or RecursiveShape => [shape],
            AnyJsonShape => [new AnyJsonShape(ObjectOrArray: true)],
            UnionShape union => [.. union.Variants.SelectMany(Kept)],
            _ => [],
        };
    }

    /// <summary>A URL a call may send, a number in it shown as <c>{number}</c>.</summary>
    private static string Shown(SentText url) =>
        string.Concat(url.Parts.Select(p => p is SentText.Literal literal ? literal.Text : "{number}"));
}
