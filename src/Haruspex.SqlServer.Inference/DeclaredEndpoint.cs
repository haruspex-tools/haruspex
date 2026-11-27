using Haruspex.Collections;
using Haruspex.Ir;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// An external REST endpoint the config file declares a response for, in the IR's terms: the type of the body a call of it reads
/// into <c>result</c>. Inference takes it as given, as it takes the model; where it comes from (the config file, in
/// Haruspex.Config) is not its concern (docs/decisions/0140-a-rest-response-is-typed-from-a-schema-declared-for-its-endpoint.md).
/// </summary>
/// <param name="Content">What the body parses to.</param>
/// <param name="Types">The object types <paramref name="Content"/> refers to.</param>
/// <param name="Record">The declaration as the IR records it: its method, its URL and the schema it read.</param>
public sealed record DeclaredEndpoint(IrType Content, EquatableMap<string, IrObjectType> Types, IrEndpointDeclaration Record)
{
    /// <summary>
    /// Why <paramref name="address"/> can't be an endpoint's URL; null when it can: an absolute <c>https</c> URL with its host, each
    /// <c>{name}</c> in its path or query, no two side by side (<see cref="UrlTemplate"/>).
    /// </summary>
    public static string? Problem(string address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return UrlTemplate.Parse(address, out var problem) is null ? problem : null;
    }
}
