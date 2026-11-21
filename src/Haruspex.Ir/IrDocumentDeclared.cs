namespace Haruspex.Ir;

/// <summary>
/// Where a document first says what only a declaration of a column's JSON gives (0.28), other than the declaration itself: any JSON
/// (<see cref="IrAnyJson"/>), or a scalar fact a declaration states (<see cref="IrFactSourceKind.Declared"/>). What
/// <see cref="IrDocument.Declares"/> asks, and where the reader says such a node is out of place in an older document.
/// </summary>
internal static class IrDocumentDeclared
{
    /// <summary>The path of the first such node, in the order of the file, with what it is; null when there is none.</summary>
    public static (string Path, string What)? First(IrDocument document)
    {
        foreach (var (path, node) in IrDocumentScalars.Nodes(document))
        {
            if (node is IrAnyJson)
                return (path, "any JSON (kind \"any-json\")");
            if (node is IrScalar { Facets: { } facets } && Declared(facets) is { } fact)
                return (path + "." + fact, "a fact a declaration states (from \"declared\")");
        }

        return null;
    }

    /// <summary>
    /// The name of the first facet whose source is a declaration, as the file writes it, or null: a declaration states a minimum
    /// length (<c>minLength</c>) and a value set (<c>enum</c>, <c>const</c>), and no other facet.
    /// </summary>
    private static string? Declared(IrFacets facets) =>
        facets.MinLength?.From.Kind == IrFactSourceKind.Declared ? "minLength"
        : facets.Values?.From.Any(f => f.Kind == IrFactSourceKind.Declared) == true ? "values"
        : null;
}
