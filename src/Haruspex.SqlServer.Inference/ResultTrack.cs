using System.Collections.Immutable;
using System.Text;
using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// The result sets the paths reaching a point have returned. Result sets are positional: a client receives them in order,
/// and one that does not run is not there at all, so a later one takes its place (observed). So the track keeps, for each
/// position, every result some path returned there, and the numbers of results the paths have returned so far. A result
/// returned now lands at every position some path is at.
/// </summary>
internal sealed class ResultTrack
{
    private ResultTrack(ImmutableList<ImmutableList<Produced>> positions, ImmutableSortedSet<int> counts)
    {
        Positions = positions;
        Counts = counts;
    }

    public static ResultTrack None { get; } = new([], [0]);

    /// <summary>For each position, what the paths returned there.</summary>
    public ImmutableList<ImmutableList<Produced>> Positions { get; }

    /// <summary>How many result sets the paths have returned: one number per group of paths.</summary>
    public ImmutableSortedSet<int> Counts { get; }

    public ResultTrack Append(ResultShape result, SourceLocation at)
    {
        var produced = new Produced(result, at, Key(result, at));
        var positions = Positions;
        foreach (var count in Counts)
        {
            while (positions.Count <= count)
                positions = positions.Add([]);
            if (!positions[count].Any(p => p.Key == produced.Key))
                positions = positions.SetItem(count, positions[count].Add(produced));
        }

        return new ResultTrack(positions, [.. Counts.Select(c => c + 1)]);
    }

    /// <summary>
    /// The results of a call appended: a callee's result sets arrive in the caller's stream where it is called, in order
    /// (observed), so the callee's k-th lands at every position some path of the caller is at, plus k.
    /// </summary>
    public ResultTrack Then(ResultTrack callee)
    {
        if (callee.Positions.Count == 0 && callee.Counts.SetEquals([0]))
            return this;
        var positions = Positions;
        foreach (var count in Counts)
        {
            for (var i = 0; i < callee.Positions.Count; i++)
            {
                while (positions.Count <= count + i)
                    positions = positions.Add([]);
                foreach (var produced in callee.Positions[i])
                {
                    if (!positions[count + i].Any(p => p.Key == produced.Key))
                        positions = positions.SetItem(count + i, positions[count + i].Add(produced));
                }
            }
        }

        return new ResultTrack(positions, [.. Counts.SelectMany(c => callee.Counts.Select(k => c + k))]);
    }

    /// <summary>The same results, stopped after any number of them: what a client has received when an error cuts a call short.</summary>
    public ResultTrack Prefixes() => new(Positions, [.. Enumerable.Range(0, Counts.Max + 1)]);

    /// <summary>Every result some path returns, at any position.</summary>
    public IEnumerable<Produced> All => Positions.SelectMany(p => p);

    public ResultTrack Join(ResultTrack other)
    {
        var positions = Positions;
        for (var i = 0; i < other.Positions.Count; i++)
        {
            if (positions.Count <= i)
                positions = positions.Add([]);
            foreach (var produced in other.Positions[i])
            {
                if (!positions[i].Any(p => p.Key == produced.Key))
                    positions = positions.SetItem(i, positions[i].Add(produced));
            }
        }

        return new ResultTrack(positions, Counts.Union(other.Counts));
    }

    public void Write(StringBuilder text)
    {
        foreach (var position in Positions)
            text.Append('[').AppendJoin(';', position.Select(p => p.Key)).Append(']');
        text.Append('#').AppendJoin(',', Counts);
    }

    private static string Key(ResultShape result, SourceLocation at)
    {
        var text = new StringBuilder();
        text.Append(at).Append('|').Append(result.Encoding).Append('|').Append(ShapeText.Of(result.Shape)).Append('|');
        foreach (var reason in result.AbsentBecause)
            text.Append(reason.Rule).Append('@').Append(reason.At).Append(',');
        return text.ToString();
    }
}
