namespace Haruspex.Ir;

/// <summary>
/// Which fields generated code explains in a comment (<c>"comments"</c> in the config file): none, the surprises
/// (<see cref="IrSurprises"/>), or every field. The notes that state facts a check rests on are not these, and are written
/// whatever this says (<c>docs/decisions/0079-a-surprise-is-explained-where-it-is-written.md</c>).
/// </summary>
public enum IrComments
{
    /// <summary>No explanation is written.</summary>
    None,

    /// <summary>A field that is a surprise is explained, and every unknown: the default.</summary>
    Surprises,

    /// <summary>Every field is explained.</summary>
    All,
}
