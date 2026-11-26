using Haruspex.Collections;

namespace Haruspex.Ir;

/// <summary>
/// One difference between what two documents claim (<see cref="IrComparison"/>): where it is, what kind of change it is, and
/// the claim before and after, in words.
/// </summary>
/// <remarks>A value the comparison yields, not part of the format: nothing in the IR file is one.</remarks>
/// <param name="Kind">How the claim changed.</param>
/// <param name="Object">The id of the object it is in, such as <c>shop.list_orders</c>; empty for a claim of the whole document.</param>
/// <param name="Path">
/// Where in the object, in the IR's path syntax (<see cref="IrPath"/>) with the object's own parts before it:
/// <c>results[0][].lines[].qty</c>, <c>parameters["@order_id"]</c>, <c>results</c>; empty for the object itself. For a claim of
/// the whole document, where in it: <c>source.session</c>, <c>folds["…"]</c>.
/// </param>
/// <param name="Before">The claim in the earlier document, in words, when the kind needs it; null otherwise.</param>
/// <param name="After">The claim in the later document, in words, when the kind needs it; null otherwise.</param>
public readonly record struct IrChange(IrChangeKind Kind, string Object, string Path, string? Before = null, string? After = null)
{
    /// <summary>
    /// For a value that became nullable, the reasons the later document gives it, each with its place: where in the source the
    /// change comes from, which a report can point at rather than at the object. Empty for every other kind of change, whose
    /// place the IR doesn't record (a field's own definition has no location).
    /// </summary>
    public EquatableArray<IrNullReason> NullableBecause { get; init; } = [];

    /// <summary>What changed, as a phrase that follows the place: <c>became nullable</c>, <c>type changed from string to number</c>.</summary>
    public string Description => Kind switch
    {
        IrChangeKind.ObjectAdded or IrChangeKind.ResultAdded => "added",
        IrChangeKind.ObjectRemoved or IrChangeKind.ResultRemoved => "removed",
        IrChangeKind.ParameterAdded or IrChangeKind.FieldAdded => $"added ({After})",
        IrChangeKind.ParameterRemoved or IrChangeKind.FieldRemoved => $"removed ({Before})",
        IrChangeKind.ObjectRenamed => $"renamed from {Before} to {After}",
        IrChangeKind.ObjectKindChanged => $"kind changed from {Before} to {After}",
        IrChangeKind.TextChanged => "the text it runs changed",
        IrChangeKind.ReturnsCodeChanged => After == "true" ? "may now end with a return code other than 0" : "now always ends with the return code 0",
        IrChangeKind.ParametersReordered or IrChangeKind.FieldsReordered => $"{(Kind == IrChangeKind.FieldsReordered ? "fields" : "parameters")} reordered from {Before} to {After}",
        IrChangeKind.ParameterDefaultChanged => After == "true" ? "now has a default" : "no longer has a default",
        IrChangeKind.ParameterDirectionChanged => After == "true" ? "is now an output parameter" : "is no longer an output parameter",
        IrChangeKind.ResultsBecameUnknown => "became unknown: the result sets can't be listed",
        IrChangeKind.ResultsBecameKnown => $"became known: {After}",
        IrChangeKind.ResultRenamed => $"name changed from {Before ?? "none"} to {After ?? "none"}",
        IrChangeKind.EncodingChanged => $"encoding changed from {Before} to {After}",
        IrChangeKind.CardinalityChanged => $"rows changed from {Before} to {After}",
        IrChangeKind.BecameMayBeAbsent => "may now be absent",
        IrChangeKind.BecameAlwaysPresent => "now always arrives",
        IrChangeKind.BecameNullable => "became nullable",
        IrChangeKind.BecameNonNull => "became non-null",
        IrChangeKind.PresenceChanged => $"presence changed from {Before} to {After}",
        IrChangeKind.TypeChanged => $"type changed from {Before} to {After}",
        IrChangeKind.BecameUnknown => $"became unknown ({After}); it was {Before}",
        IrChangeKind.BecameKnown => $"became known: {After}",
        IrChangeKind.TypeRenamed => $"type renamed from {Before} to {After}",
        IrChangeKind.SqlTypeChanged => $"SQL type changed from {Before} to {After}",
        IrChangeKind.TypeUnproven => $"type {After} can't be shown unchanged",
        IrChangeKind.TableTypeChanged => (Before, After) switch
        {
            (null, _) => $"now a table type, {After}",
            (_, null) => $"no longer a table type ({Before})",
            _ => $"table type changed from {Before} to {After}",
        },
        IrChangeKind.LengthChanged => $"length changed from {Before} to {After}",
        IrChangeKind.MinLengthChanged => $"minimum length changed from {Before} to {After}",
        IrChangeKind.DigitsChanged => $"digits changed from {Before} to {After}",
        IrChangeKind.MinChanged => $"lower bound changed from {Before} to {After}",
        IrChangeKind.MaxChanged => $"upper bound changed from {Before} to {After}",
        IrChangeKind.ItemsBoundsChanged => $"number of items changed from {Before} to {After}",
        IrChangeKind.ValuesAdded => $"value set grew: added {After}",
        IrChangeKind.ValuesRemoved => $"value set shrank: removed {Before}",
        IrChangeKind.ValueSetAdded => $"now drawn from a value set ({After})",
        IrChangeKind.ValueSetRemoved => $"no longer drawn from a value set (was {Before})",
        IrChangeKind.ValueMatchChanged => $"value set's match changed from {Before} to {After}",
        IrChangeKind.BuildsChanged => After == "true" ? "now declared a build" : "no longer declared a build",
        IrChangeKind.DialectChanged or IrChangeKind.SessionChanged or IrChangeKind.FoldChanged or IrChangeKind.ColumnDeclarationChanged => $"changed from {Before} to {After}",
    };

    /// <summary>The change as one line: the object, the path and the description, such as <c>shop.list_orders results[0][].note: became nullable</c>.</summary>
    public override string ToString()
    {
        var place = string.Join(' ', new[] { Object, Path }.Where(part => part.Length > 0));
        return $"{place}: {Description}";
    }
}
