using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Semantics;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// Which declarations of the config file each body reads, and which bodies each body reaches (a function, a view, a table-valued
/// function, a procedure it calls), so that an object's claims list every declaration its results may rest on, however deep the
/// read: a column declared to hold JSON (<see cref="IrOverrideSetting.ColumnJson"/>), or the response declared for an endpoint
/// (<see cref="IrOverrideSetting.EndpointJson"/>).
/// </summary>
/// <remarks>
/// <para>
/// A summary is worked out once per document and then reused, so what a body reads is recorded by the body, and what an object
/// rests on is found afterwards by following what each body reached, rather than by the first walk that met it.
/// </para>
/// <para>
/// A body is known by where it is and by the arguments its walk was given (<see cref="CallContext.Arguments"/>): a wrapper of a
/// REST endpoint walked for one call reads the endpoint that call's text names, and another call of it may read another or none.
/// So a read of an endpoint, and a call that passes text, are recorded for the walk that made them, and an object lists only what
/// its own calls read. What the rest of a body reads (a declared column, a function, a view) doesn't depend on those arguments,
/// so it is recorded for the body, and holds for every walk of it.
/// </para>
/// </remarks>
internal sealed class DeclarationReads
{
    private readonly Dictionary<Walk, SortedSet<(IrOverrideSetting Setting, string Name)>> read = [];
    private readonly Dictionary<Walk, HashSet<Walk>> reached = [];

    /// <summary>A body as one walk of it saw it: where it is, and the text its call passed it.</summary>
    /// <param name="Body">Where the body is.</param>
    /// <param name="Arguments">
    /// The text its call passed it, empty for the outermost walk and for a call that passed none; null for what every walk of it sees.
    /// </param>
    internal sealed record Walk(SourceSpan Body, EquatableMap<LocalName, CallContext.Passed>? Arguments)
    {
        /// <summary>The body as every walk of it sees it.</summary>
        public static Walk Of(SourceSpan body) => new(body, null);
    }

    /// <summary>Records that the body <paramref name="reader"/> reads the declaration <paramref name="name"/>, in every walk of it.</summary>
    public void Read(SourceSpan reader, IrOverrideSetting setting, string name) => Read(Walk.Of(reader), setting, name);

    /// <summary>Records that one walk of a body reads the declaration <paramref name="name"/>.</summary>
    public void Read(Walk reader, IrOverrideSetting setting, string name)
    {
        if (!read.TryGetValue(reader, out var names))
            read[reader] = names = [];
        names.Add((setting, name));
    }

    /// <summary>Records that the body <paramref name="from"/> reads or calls the body <paramref name="to"/>, in every walk of each.</summary>
    public void Reached(SourceSpan from, SourceSpan to) => Reached(Walk.Of(from), Walk.Of(to));

    /// <summary>Records that one walk of a body calls another body with the text it passes.</summary>
    public void Reached(Walk from, Walk to)
    {
        if (!reached.TryGetValue(from, out var targets))
            reached[from] = targets = [];
        targets.Add(to);
    }

    /// <summary>
    /// The declarations of a kind the body <paramref name="top"/> reads, as the outermost call, itself or through any body it
    /// reaches, ordinal order. Each walk reached reads what its body reads in every walk too.
    /// </summary>
    public IReadOnlyList<string> ReadFrom(SourceSpan top, IrOverrideSetting setting)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<Walk>();
        var pending = new Stack<Walk>([new Walk(top, [])]);
        while (pending.TryPop(out var walk))
        {
            foreach (var at in (Walk[])[walk, Walk.Of(walk.Body)])
            {
                if (!seen.Add(at))
                    continue;
                names.UnionWith((read.GetValueOrDefault(at) ?? []).Where(r => r.Setting == setting).Select(r => r.Name));
                foreach (var next in reached.GetValueOrDefault(at) ?? [])
                    pending.Push(next);
            }
        }

        return [.. names];
    }
}
