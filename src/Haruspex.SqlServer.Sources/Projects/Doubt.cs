using Haruspex.Collections;
using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Sources.Projects;

/// <summary>
/// Something in a project file that isn't evaluated, and so may make a value other than what was read: a property function, a
/// property defined nowhere, a condition that can't be evaluated. It travels with every value it touches
/// (<see cref="Evaluated"/>, <see cref="Truth"/>) and is reported only when a value the reader reads carries it, so that what
/// touches nothing read says nothing (decision 0044).
/// </summary>
/// <param name="At">Where it is in the project file or a file it imports.</param>
/// <param name="Why">What isn't evaluated, as a clause: "the property function $([System.IO.Path]::Combine(…)) isn't evaluated".</param>
internal sealed record Doubt(SourceLocation At, string Why)
{
    /// <summary>The doubts of two values together, each once, in the order met.</summary>
    public static EquatableArray<Doubt> Union(IEnumerable<Doubt> first, IEnumerable<Doubt> second) => [.. first.Concat(second).Distinct()];

    /// <summary>
    /// Reports that <paramref name="what"/> isn't read because of <paramref name="doubts"/>: one warning, where the first doubt is,
    /// naming every one; and not again when the same is already reported, so a doubt that touches the same thing twice (in a
    /// Debug and a Release read, or two settings) names itself once.
    /// </summary>
    /// <param name="diagnostics">Where it is reported.</param>
    /// <param name="what">What isn't read, as the subject of a sentence: "The Build item", "DefaultCollation, a setting of the database,".</param>
    /// <param name="doubts">Why: at least one.</param>
    public static void Report(ICollection<Diagnostic> diagnostics, string what, IEnumerable<Doubt> doubts)
    {
        var all = doubts.Distinct().ToList();
        var reported = SourceCodes.PartNotRead.At(all[0].At, $"{what} isn't read: {string.Join("; ", all.Select(d => d.Why))}.");
        if (!diagnostics.Contains(reported))
            diagnostics.Add(reported);
    }
}
