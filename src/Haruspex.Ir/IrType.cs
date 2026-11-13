namespace Haruspex.Ir;

/// <summary>A type node in the IR.</summary>
/// <remarks>
/// The set of type nodes is closed: every one is <see cref="IrScalar"/>, <see cref="IrArray"/>, <see cref="IrTuple"/>,
/// <see cref="IrRecord"/>, <see cref="IrUnion"/>, <see cref="IrRef"/>, <see cref="IrJsonText"/>, <see cref="IrImport"/> or
/// <see cref="IrUnknownType"/>, so an emitter can handle all of them. README.md in this folder describes the format and the
/// reasons behind it.
/// </remarks>
public abstract record IrType
{
    /// <summary>Only this assembly derives a case, so the set is closed (docs/decisions/0031-the-model-is-closed-hierarchies-of-records.md).</summary>
    private protected IrType()
    {
    }
}
