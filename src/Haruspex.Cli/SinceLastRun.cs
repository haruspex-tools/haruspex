using Haruspex.Collections;
using Haruspex.Ir;

namespace Haruspex.Cli;

/// <summary>
/// What changed since the last run of <c>generate</c>: the IR in the output folder before this run wrote over it, compared with
/// the one it wrote. A command run many times a day should say what is new, not repeat what its reader has seen, so a run that
/// can compare reports the difference, and one that can't (a first run, an IR of another version or one that doesn't read) says
/// why and gives the whole summary. Claims are compared as <c>check</c> compares them (<see cref="IrComparison"/>); unknowns by
/// their object, their path and their reason, so a procedure moved down its file keeps its unknowns.
/// </summary>
/// <remarks>Like each value handed from one step to the next, a class: nothing compares two.</remarks>
internal abstract class SinceLastRun
{
    private SinceLastRun()
    {
    }

    /// <summary>Nothing to compare with: the output folder held no IR.</summary>
    internal sealed class FirstRun : SinceLastRun;

    /// <summary>An IR that can't be compared with this one.</summary>
    /// <param name="why">Why, as a clause: <c>it is IR 0.21, and this haruspex writes IR 0.22</c>.</param>
    internal sealed class NotCompared(string why) : SinceLastRun
    {
        /// <summary>Why, as a clause.</summary>
        public string Why { get; } = why;
    }

    /// <summary>The difference from the IR the output folder held.</summary>
    internal sealed class Compared(EquatableArray<IrChange> claims, EquatableArray<NotedUnknown> added, EquatableArray<IrUnknown> resolved, int unchanged, EquatableArray<string> rewritten)
        : SinceLastRun
    {
        /// <summary>
        /// The script files whose text the IR now records anew (<see cref="IrSource.Files"/>): edited, or newly or no longer pointed
        /// into, by path. The IR changes for each though no claim does, so an edit to a comment inside a procedure changes the
        /// committed IR, and the run says so rather than that nothing changed.
        /// </summary>
        public EquatableArray<string> Recorded { get; init; } = [];

        /// <summary>
        /// Whether the IR's text differs from the last run's though no claim does: what it records of where its claims came from
        /// (a location, a source's path, a file's hash, the build that wrote it) changed, or another version wrote it.
        /// </summary>
        public bool IrRewritten { get; init; }

        /// <summary>Each change of claim, as <c>check</c> would report it.</summary>
        public EquatableArray<IrChange> Claims { get; } = claims;

        /// <summary>The unknowns this run has that the last didn't, with their notes.</summary>
        public EquatableArray<NotedUnknown> New { get; } = added;

        /// <summary>The unknowns the last run had that this one doesn't.</summary>
        public EquatableArray<IrUnknown> Resolved { get; } = resolved;

        /// <summary>How many of this run's unknowns the last run had too.</summary>
        public int Unchanged { get; } = unchanged;

        /// <summary>
        /// The emitted files (by name) whose text differs from the last run's, line endings aside, or that it didn't write. Their
        /// claims may be the same: a comment written from a reason, or the output of another version, changes the text alone.
        /// </summary>
        public EquatableArray<string> Rewritten { get; } = rewritten;

        /// <summary>Whether nothing changed: the same claims, the same unknowns and the same text in every emitted file.</summary>
        public bool Nothing => Claims.Count == 0 && New.Count == 0 && Resolved.Count == 0 && Rewritten.Count == 0 && !IrRewritten;
    }

    /// <summary>What changed from <paramref name="earlier"/>, the folder before this run, to <paramref name="module"/>, which it wrote.</summary>
    /// <param name="earlier">What the output folder held.</param>
    /// <param name="module">The module written.</param>
    /// <param name="unknowns">The module's unknowns, noted.</param>
    public static SinceLastRun Of(EarlierOutput earlier, EmittedModule module, EquatableArray<NotedUnknown> unknowns)
    {
        var name = Path.GetFileName(module.IrPath);
        if (earlier.Unreadable.FirstOrDefault() is { Key: { } file, Value: var problem })
            return new NotCompared($"the earlier {Path.GetFileName(file)} can't be read ({problem.TrimEnd('.')})");
        return earlier.Ir switch
        {
            EarlierIr.Absent => new FirstRun(),
            EarlierIr.OtherVersion { Version: var version } => new NotCompared($"the earlier {name} is IR {version}, and this haruspex writes IR {module.Document.Version}"),
            EarlierIr.Unreadable unreadable => new NotCompared($"the earlier {name} can't be read{(unreadable.AsIr ? " as IR" : "")} ({unreadable.Problem.TrimEnd('.')})"),
            EarlierIr.Readable { Document: var before } => Compare(before, earlier, module, unknowns),

            // See EarlierIr: the analyser that would check this switch covers each case doesn't look at this assembly.
            _ => throw new InvalidOperationException($"An earlier IR of no known kind: {earlier.Ir}."),
        };
    }

    private static Compared Compare(IrDocument before, EarlierOutput earlier, EmittedModule module, EquatableArray<NotedUnknown> unknowns)
    {
        // Unknowns are a multiset by their key: two at one path for one reason are two, and one going leaves the other.
        var left = before.Unknowns.GroupBy(Key).ToDictionary(g => g.Key, g => g.Count());
        var added = new List<NotedUnknown>();
        var unchanged = 0;
        foreach (var noted in unknowns)
        {
            var key = Key(noted.Unknown);
            if (left.TryGetValue(key, out var count) && count > 0)
            {
                left[key] = count - 1;
                unchanged++;
            }
            else
            {
                added.Add(noted);
            }
        }

        var kept = unknowns.Select(u => Key(u.Unknown)).GroupBy(k => k).ToDictionary(g => g.Key, g => g.Count());
        var resolved = new List<IrUnknown>();
        foreach (var unknown in before.Unknowns)
        {
            var key = Key(unknown);
            if (kept.TryGetValue(key, out var count) && count > 0)
                kept[key] = count - 1;
            else
                resolved.Add(unknown);
        }

        var rewritten = module.Files
            .Where((file, i) => earlier.Files[i] is not { } text || text.Replace("\r\n", "\n", StringComparison.Ordinal) != file.Text)
            .Select(file => file.Name);
        var claims = IrComparison.Compare(before, module.Document);
        return new Compared(claims, [.. added], [.. resolved], unchanged, [.. rewritten])
        {
            IrRewritten = claims.Count == 0 && IrJsonWriter.Write(before) != IrJsonWriter.Write(module.Document),
            Recorded = [.. Rehashed(before.Source, module.Document.Source).Concat(Rehashed(before.Source.Baseline, module.Document.Source.Baseline)).Distinct().Order(StringComparer.Ordinal)],
        };
    }

    /// <summary>The script files whose recorded hash differs between two sources, or that only one records.</summary>
    private static IEnumerable<string> Rehashed(IrSource? before, IrSource? after)
    {
        var was = before?.Files ?? [];
        var now = after?.Files ?? [];
        return was.Keys.Union(now.Keys).Where(path => !was.TryGetValue(path, out var a) || !now.TryGetValue(path, out var b) || a != b);
    }

    /// <summary>What makes two unknowns the same one: where it is in which object, and why. Not its detail, nor its place in the SQL.</summary>
    private static (string Object, string Path, IrUnknownReason Reason) Key(IrUnknown unknown) => (unknown.Object, unknown.Path, unknown.Reason);
}
