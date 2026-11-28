namespace Haruspex.Ir;

/// <summary>
/// Where a document first holds a value of the session's state (0.38): a reason of the rule <see cref="IrNullRule.SessionState"/>.
/// What <see cref="IrDocument.HoldsSessionState"/> asks, and where the reader says one is out of place in an older document.
/// </summary>
internal static class IrDocumentSessionState
{
    /// <summary>The path of the first such reason's rule; null when there is none.</summary>
    public static string? First(IrDocument document) =>
        IrDocumentReasons.Of(document).Where(r => r.Reason.Rule == IrNullRule.SessionState).Select(r => r.Path + ".rule").FirstOrDefault();
}
