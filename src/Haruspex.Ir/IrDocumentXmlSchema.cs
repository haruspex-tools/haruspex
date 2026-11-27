namespace Haruspex.Ir;

/// <summary>
/// Where a document first holds a fact an XML schema collection proves (0.35): a facet whose source is
/// <see cref="IrFactSourceKind.XmlSchema"/>. What <see cref="IrDocument.HoldsSchemaFacts"/> asks, and where the reader says one is out
/// of place in an older document.
/// </summary>
internal static class IrDocumentXmlSchema
{
    /// <summary>The path of the first such facet, in the order of the file; null when there is none.</summary>
    public static string? First(IrDocument document)
    {
        foreach (var (path, node) in IrDocumentScalars.Nodes(document))
        {
            if (node is IrScalar { Facets: { } facets } && Facet(facets) is { } name)
                return path + ".facets." + name;
        }

        return null;
    }

    /// <summary>The name of the first facet a schema collection proves, as the file writes it, or null.</summary>
    private static string? Facet(IrFacets facets) =>
        facets.Length is { From.Kind: IrFactSourceKind.XmlSchema } length ? (length.IsFixed ? "length" : "maxLength")
        : facets.MinLength is { From.Kind: IrFactSourceKind.XmlSchema } ? "minLength"
        : facets.Digits is { From.Kind: IrFactSourceKind.XmlSchema } ? "digits"
        : facets.Min is { From.Kind: IrFactSourceKind.XmlSchema } ? "min"
        : facets.Max is { From.Kind: IrFactSourceKind.XmlSchema } ? "max"
        : facets.Values?.From.Any(f => f.Kind == IrFactSourceKind.XmlSchema) == true ? "values"
        : null;
}
