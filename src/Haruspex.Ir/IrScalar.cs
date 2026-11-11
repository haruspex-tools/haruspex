namespace Haruspex.Ir;

/// <summary>A scalar with its logical kind and the SQL type it came from.</summary>
/// <param name="Kind">The logical kind, which every emitter understands.</param>
/// <param name="Format">A refinement of the kind (see <see cref="IrFormats"/>), or null.</param>
/// <param name="Sql">
/// The source's own type, as written, such as <c>decimal(19,2)</c>. Informational. Null when the value has no SQL type: in
/// results the config file declares (the <c>declared</c> null reason marks them), whose declaration is a JSON Schema.
/// </param>
/// <param name="Facets">
/// What the database guarantees about the value beyond its kind: lengths, digits, bounds and value sets, each with
/// where it came from. Null when nothing more is known, which claims nothing.
/// </param>
public sealed record IrScalar(IrScalarKind Kind, string? Format, string? Sql, IrFacets? Facets = null) : IrType;
