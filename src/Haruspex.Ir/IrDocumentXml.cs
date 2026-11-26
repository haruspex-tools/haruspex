namespace Haruspex.Ir;

/// <summary>
/// Where a document first holds an XML value (0.34): a scalar of kind <see cref="IrScalarKind.Xml"/>, then a reason of the rule
/// <see cref="IrNullRule.XmlValue"/>, which a value read from XML has though the document holds no XML itself. What
/// <see cref="IrDocument.HoldsXml"/> asks, and where the reader says one is out of place in an older document.
/// </summary>
internal static class IrDocumentXml
{
    /// <summary>The path of the first XML value's kind, then of the first such reason's rule; null when there is none.</summary>
    public static string? First(IrDocument document) =>
        IrDocumentScalars.Of(document).Where(s => s.Scalar.Kind == IrScalarKind.Xml).Select(s => s.Path + ".kind").FirstOrDefault()
        ?? IrDocumentReasons.Of(document).Where(r => r.Reason.Rule == IrNullRule.XmlValue).Select(r => r.Path + ".rule").FirstOrDefault();
}
