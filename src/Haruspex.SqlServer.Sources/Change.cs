using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Sources;

/// <summary>The first statement that changes a procedure or function after its definition, and, for an <c>ALTER</c>, the definition it gives.</summary>
/// <typeparam name="T">The kind of module.</typeparam>
/// <param name="Why">The statement.</param>
/// <param name="Altered">The definition an <c>ALTER</c> gives; null for a <c>DROP</c>, which gives none.</param>
internal sealed record Change<T>(Redefinition Why, T? Altered)
    where T : Module;
