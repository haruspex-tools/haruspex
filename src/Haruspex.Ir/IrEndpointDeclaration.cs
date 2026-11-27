using Haruspex.Collections;

namespace Haruspex.Ir;

/// <summary>
/// The response the config file declares an external REST endpoint to send (0.36): the type of the body a call of
/// <c>sys.sp_invoke_external_rest_endpoint</c> reads into its envelope's <c>result</c> when it calls that endpoint. Recorded so that
/// a reader can tell what was declared from what was inferred, and so that a change to a schema file is a change to the document.
/// Why: docs/decisions/0140-a-rest-response-is-typed-from-a-schema-declared-for-its-endpoint.md.
/// </summary>
/// <param name="Method">The HTTP method, in upper case: <c>GET</c>.</param>
/// <param name="Address">
/// The URL as the declaration writes it (<c>url</c>), each <c>{name}</c> standing for one segment or one value of the query: a
/// template, not a URL that can be parsed.
/// </param>
/// <param name="Schema">
/// The schema file, relative to the folder of the config file that declares it and <c>/</c>-separated, so the same on every machine.
/// </param>
/// <param name="Pointer">The JSON Pointer into the file of the schema used (<c>/$defs/plan</c>), or null for the whole file.</param>
/// <param name="Files">
/// The SHA-256 of every schema file the declaration read, by path, as <see cref="IrColumnDeclaration.Files"/> gives them.
/// </param>
public sealed record IrEndpointDeclaration(string Method, string Address, string Schema, string? Pointer, EquatableMap<string, string> Files)
{
    /// <summary>The endpoint as a claim names it (<see cref="IrOverride.Via"/>): its method, a space and its URL.</summary>
    public string Endpoint => $"{Method} {Address}";
}
