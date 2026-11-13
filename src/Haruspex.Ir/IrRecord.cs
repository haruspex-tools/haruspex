using Haruspex.Collections;

namespace Haruspex.Ir;

/// <summary>
/// An object whose keys are data, not a fixed set, as <c>json_objectagg</c> builds one: every key is a string, and
/// every value has one type.
/// </summary>
/// <param name="Values">The values' type when they are not null.</param>
/// <param name="ValuesNullableBecause">Why a value can be null; empty when no value is ever null.</param>
public sealed record IrRecord(IrType Values, EquatableArray<IrNullReason> ValuesNullableBecause) : IrType
{
    /// <summary>Whether a value can be null: true exactly when <see cref="ValuesNullableBecause"/> gives a reason.</summary>
    public bool ValuesNullable => ValuesNullableBecause.Count > 0;
}
