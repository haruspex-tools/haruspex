using Haruspex.Collections;

namespace Haruspex.SqlServer.Sources.Projects;

/// <summary>
/// A condition, evaluated: whether it holds as far as it is evaluated, and the doubts that may make it hold or not otherwise. A
/// condition that can't be evaluated at all is false with a doubt saying so, so whatever it guards is doubted, never assumed.
/// </summary>
/// <param name="Holds">Whether it holds, taking every doubted value as its text.</param>
/// <param name="Doubts">What may change that; empty when nothing does.</param>
internal sealed record Truth(bool Holds, EquatableArray<Doubt> Doubts)
{
    /// <summary>A condition known to hold: the one an element without a condition has.</summary>
    public static Truth Always { get; } = new(Holds: true, []);

    /// <summary>Whether this and <paramref name="other"/> both hold: an element's condition and its group's.</summary>
    public Truth And(Truth other) => new(Holds && other.Holds, Doubt.Union(Doubts, other.Doubts));
}
