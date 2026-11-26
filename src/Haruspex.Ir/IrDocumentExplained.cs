namespace Haruspex.Ir;

/// <summary>
/// Where a document first says what only 0.29 says: what makes a reason so (a <see cref="IrNullReason.Detail"/>), or a minimum
/// length in code points (<see cref="IrMinLength.Unit"/>). What <see cref="IrDocument.Explains"/> asks, and where the reader says
/// such a thing is out of place in an older document.
/// </summary>
internal static class IrDocumentExplained
{
    /// <summary>The path of the first reason with a detail, then of the first minimum in code points; null when there is none.</summary>
    public static string? First(IrDocument document) =>
        IrDocumentReasons.Of(document).Where(r => r.Reason.Detail is not null).Select(r => r.Path + ".detail").FirstOrDefault()
        ?? IrDocumentScalars.Of(document).Where(s => s.Scalar.Facets?.MinLength?.Unit == IrLengthUnit.CodePoints).Select(s => s.Path + ".minLength.unit").FirstOrDefault();
}
