namespace Haruspex.Ir;

/// <summary>How the application's generated code reacts to what it doesn't know, which decides whether an addition fits (<see cref="IrFit"/>).</summary>
/// <remarks>A value the operation is given, not part of the format: nothing in the IR file is one.</remarks>
public readonly record struct IrFitOptions
{
    /// <summary>
    /// Whether the application's schemas refuse a key their type doesn't list, and its functions a result set after those they
    /// know (<c>"objects": "strict"</c>); by default they set both aside (docs/decisions/0008-objects-strip-unknown-keys-by-default.md,
    /// docs/decisions/0063-a-function-ignores-result-sets-after-those-it-knows.md).
    /// </summary>
    public bool StrictObjects { get; init; }
}
