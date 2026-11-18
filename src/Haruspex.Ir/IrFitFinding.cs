namespace Haruspex.Ir;

/// <summary>
/// One difference that matters to an application built against one document meeting a database described by another
/// (<see cref="IrFit"/>): the change, from the application's claim (<see cref="IrChange.Before"/>) to the database's
/// (<see cref="IrChange.After"/>), what the application does about it, and why.
/// </summary>
/// <remarks>A value the operation yields, not part of the format: nothing in the IR file is one.</remarks>
/// <param name="Change">Where the difference is and what it is, worded as <see cref="IrComparison"/> words a change.</param>
/// <param name="Verdict">Whether the application fails on it or sets it aside.</param>
/// <param name="Why">Why, in the application's terms: <c>the application's schema refuses null</c>.</param>
public readonly record struct IrFitFinding(IrChange Change, IrFitVerdict Verdict, string Why)
{
    /// <summary>The finding as one line: <c>refused: shop.list_orders results[0][].note: became nullable</c>.</summary>
    public override string ToString() => $"{(Verdict == IrFitVerdict.Refused ? "refused" : "ignored")}: {Change}";
}
