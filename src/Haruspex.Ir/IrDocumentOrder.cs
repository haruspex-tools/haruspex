namespace Haruspex.Ir;

/// <summary>
/// The order the IR's JSON lists a document's objects, unknowns and overrides in. Their order in the records carries no
/// meaning (a front end adds them as it meets them), so the file sorts them, and two documents that differ only in that
/// order are equal (<see cref="IrDocument.Equals(IrDocument)"/>). One definition, so the writer and equality can't disagree.
/// </summary>
/// <remarks>
/// The sorts are stable, and an unknown's detail and an override's reason are not among the keys, so two entries that tie
/// keep the order they were added in, in the file as in equality.
/// </remarks>
internal static class IrDocumentOrder
{
    /// <summary>Objects by id.</summary>
    public static IEnumerable<IrObject> Objects(IrDocument document) =>
        document.Objects.OrderBy(o => o.Id, StringComparer.Ordinal);

    /// <summary>Named types by name.</summary>
    public static IEnumerable<KeyValuePair<string, IrObjectType>> Types(IrDocument document) =>
        document.Types.OrderBy(t => t.Key, StringComparer.Ordinal);

    /// <summary>Unknowns by object, path, location and reason.</summary>
    public static IEnumerable<IrUnknown> Unknowns(IrDocument document) =>
        document.Unknowns
            .OrderBy(u => u.Object, StringComparer.Ordinal)
            .ThenBy(u => u.Path, StringComparer.Ordinal)
            .ThenBy(u => u.At, StringComparer.Ordinal)
            .ThenBy(u => IrWireNames.Of(u.Reason), StringComparer.Ordinal);

    /// <summary>Overrides by object, path, setting and the procedure they rest on.</summary>
    public static IEnumerable<IrOverride> Overrides(IrDocument document) =>
        document.Overrides
            .OrderBy(o => o.Object, StringComparer.Ordinal)
            .ThenBy(o => o.Path, StringComparer.Ordinal)
            .ThenBy(o => IrWireNames.Of(o.Setting), StringComparer.Ordinal)
            .ThenBy(o => o.Via, StringComparer.Ordinal);
}
