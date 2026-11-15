using Haruspex.Collections;

namespace Haruspex.SqlServer.Sources.Projects;

/// <summary>
/// A value of a project file as far as it is evaluated: the text MSBuild would give it if nothing doubted were there, and the
/// doubts that may make it something else. A value with no doubt is what a build gives.
/// </summary>
/// <param name="Text">The text, every property reference that could be expanded expanded.</param>
/// <param name="Doubts">What may make it other than <paramref name="Text"/>; empty when nothing does.</param>
internal sealed record Evaluated(string Text, EquatableArray<Doubt> Doubts)
{
    /// <summary>A value known for sure.</summary>
    public static Evaluated Sure(string text) => new(text, []);

    /// <summary>Whether it is what a build gives, with nothing doubted.</summary>
    public bool IsSure => Doubts.Count == 0;

    /// <summary>The same text, doubted for <paramref name="more"/> too.</summary>
    public Evaluated Doubted(IEnumerable<Doubt> more) => this with { Doubts = Doubt.Union(Doubts, more) };
}
