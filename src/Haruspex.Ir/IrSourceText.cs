namespace Haruspex.Ir;

/// <summary>
/// What a document's source means for the code written from it, in words, for the header an emitter writes, so that every
/// emitter says the same thing of the same document, each in its own place (a Zod comment, a JSON Schema description).
/// </summary>
public static class IrSourceText
{
    /// <summary>
    /// For a document combining two databases (<see cref="IrSource.Baseline"/>), what that means for its types:
    /// <c>Combined with a baseline (Shop.dacpac): results accept what either database returns, and parameters take only what both
    /// accept.</c> Null for a document of one database.
    /// </summary>
    public static string? Baseline(IrSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.Baseline is { } baseline
            ? $"Combined with a baseline{(baseline.Package is { } package ? $" ({package.Name})" : "")}: results accept what either database returns, and parameters take only what both accept."
            : null;
    }
}
