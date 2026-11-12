namespace Haruspex.Ir;

/// <summary>A scalar with its logical kind and the SQL type it came from.</summary>
/// <param name="Kind">The logical kind, which every emitter understands.</param>
/// <param name="Format">A refinement of the kind (see <see cref="IrFormat"/>), or null.</param>
/// <param name="Sql">
/// The source's own type, as written, such as <c>decimal(19,2)</c>. Informational. Null when the value has no SQL type: in
/// results the config file declares (the <c>declared</c> null reason marks them), whose declaration is a JSON Schema.
/// </param>
/// <param name="Facets">
/// What the database guarantees about the value beyond its kind: lengths, digits, bounds and value sets, each with
/// where it came from. Null when nothing more is known, which claims nothing; facets with nothing in them are kept as null.
/// </param>
public sealed record IrScalar(IrScalarKind Kind, IrFormat? Format, string? Sql, IrFacets? Facets = null) : IrType
{
    /// <summary>
    /// What the database guarantees about the value beyond its kind, or null when nothing more is known. Empty facets
    /// (<see cref="IrFacets.IsEmpty"/>) claim nothing too, and are stored as null, so that one claim has one form and two
    /// scalars that claim the same are equal.
    /// </summary>
    public IrFacets? Facets { get; init => field = Claimed(value); } = Claimed(Facets);

    private static IrFacets? Claimed(IrFacets? facets) => facets is { IsEmpty: true } ? null : facets;
}
