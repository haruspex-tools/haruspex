using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.Ir;

namespace Haruspex.Config;

/// <summary>
/// An external REST endpoint the config file declares a response for (<c>endpoints[]</c>), translated into the IR: the type of the
/// body a call of <c>sys.sp_invoke_external_rest_endpoint</c> reads into its envelope's <c>result</c> when it calls that endpoint
/// (docs/decisions/0140-a-rest-response-is-typed-from-a-schema-declared-for-its-endpoint.md). Whether its URL is a template the
/// inference reads is checked where the inference is (<c>DeclaredEndpoint.Problem</c>).
/// </summary>
/// <param name="Content">What the body parses to.</param>
/// <param name="Types">The object types <paramref name="Content"/> refers to.</param>
/// <param name="Record">The declaration as the IR records it: its method, its URL, the schema file and pointer, and every file's hash.</param>
/// <param name="At">Where the declaration is written.</param>
/// <param name="UrlAt">Where its URL is written, for a problem with it.</param>
public sealed record EndpointDeclaration(IrType Content, EquatableMap<string, IrObjectType> Types, IrEndpointDeclaration Record, JsonLocation At, JsonLocation UrlAt)
{
    /// <summary>The methods an endpoint may be declared for: those SQL Server sends that may have a body in reply.</summary>
    public static IReadOnlyList<string> Methods { get; } = ["GET", "POST", "PUT", "PATCH", "DELETE"];
}
