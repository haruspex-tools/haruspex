using Haruspex.Collections;
using Haruspex.Ir;

namespace Haruspex.SqlServer.Inference;

/// <param name="AbsentBecause">Why the result can arrive with no value at all (see <see cref="IrResult"/>).</param>
/// <param name="Cardinality">How many rows its query returns when it runs (see <see cref="IrResult.Cardinality"/>).</param>
internal sealed record ResultShape(IrEncoding Encoding, Shape Shape, EquatableArray<IrNullReason> AbsentBecause, IrCardinality Cardinality = IrCardinality.Many)
{
    /// <summary>
    /// Why the procedure's results can't be listed because of this result, when they can't: a JSON result whose text is not JSON
    /// for some callers, which no reader of the results could parse (<see cref="Typer.Result"/>). Null for every other result.
    /// </summary>
    public UnknownShape? ResultsUnknown { get; init; }
}
