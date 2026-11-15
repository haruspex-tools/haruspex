using Haruspex.Diagnostics;

namespace Haruspex.Tests.Corpus;

/// <summary>
/// The harvest: what every corpus, vendored and fetched, shows haruspex missing, ranked so the most common gap comes first. Each
/// unknown has a reason, so counting reasons across corpora turns the figures into a list of what to build next.
/// </summary>
internal static class CorpusHarvest
{
    /// <summary>One reason, summed over every corpus.</summary>
    /// <param name="Name">The reason, as the IR names it.</param>
    /// <param name="Fields">Fields unknown for it.</param>
    /// <param name="Results">Results of unknown shape for it.</param>
    /// <param name="Procedures">Procedures whose result sets can't be listed for it.</param>
    /// <param name="Parameters">Parameters of unknown type for it.</param>
    /// <param name="Corpora">How many corpora it appears in.</param>
    internal sealed record Row(string Name, int Fields, int Results, int Procedures, int Parameters, int Corpora)
    {
        /// <summary>Every place it makes something unknown, the figure the harvest is ranked by.</summary>
        public int Total => Fields + Results + Procedures + Parameters;
    }

    /// <summary>One diagnostic code, summed over every corpus.</summary>
    /// <param name="Code">The code.</param>
    /// <param name="Count">How many times it was reported.</param>
    /// <param name="Corpora">How many corpora reported it.</param>
    internal sealed record CodeRow(DiagnosticCode Code, int Count, int Corpora);

    /// <summary>
    /// Every reason across the corpora, most places first, then in ordinal order of the reason, so that the order is the same
    /// on every machine.
    /// </summary>
    public static IReadOnlyList<Row> Unknowns(IReadOnlyList<CorpusFigures> corpora)
    {
        ArgumentNullException.ThrowIfNull(corpora);
        var corporaWith = corpora.SelectMany(c => c.Unknowns.Select(r => r.Name).Distinct()).CountBy(n => n, StringComparer.Ordinal).ToDictionary(StringComparer.Ordinal);
        return [.. CorpusFigures.Sum(corpora).Unknowns
            .Select(r => new Row(r.Name, r.Fields, r.Results, r.Procedures, r.Parameters, corporaWith[r.Name]))
            .OrderByDescending(r => r.Total)
            .ThenBy(r => r.Name, StringComparer.Ordinal)];
    }

    /// <summary>Every diagnostic code reported reading the corpora, most often first, then by number.</summary>
    public static IReadOnlyList<CodeRow> Diagnostics(IReadOnlyList<IReadOnlyList<Diagnostic>> corpora)
    {
        ArgumentNullException.ThrowIfNull(corpora);
        return [.. corpora
            .SelectMany((diagnostics, corpus) => diagnostics.Select(d => (d.Code, Corpus: corpus)))
            .GroupBy(d => d.Code)
            .Select(g => new CodeRow(g.Key, g.Count(), g.Select(d => d.Corpus).Distinct().Count()))
            .OrderByDescending(r => r.Count)
            .ThenBy(r => r.Code.Number)];
    }
}
