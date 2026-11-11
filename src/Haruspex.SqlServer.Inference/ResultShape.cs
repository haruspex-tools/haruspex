using Haruspex.Ir;

namespace Haruspex.SqlServer.Inference;

/// <param name="AbsentBecause">Why the result can arrive with no value at all (see <see cref="IrResult"/>).</param>
/// <param name="Cardinality">How many rows its query returns when it runs (see <see cref="IrResult.Cardinality"/>).</param>
internal sealed record ResultShape(IrEncoding Encoding, Shape Shape, EquatableArray<IrNullReason> AbsentBecause, IrCardinality Cardinality = IrCardinality.Many);
