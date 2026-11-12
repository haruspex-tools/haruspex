namespace Haruspex.Ir;

/// <summary>
/// The named object types of a document as a graph: which types each refers to, which are reachable from a type, which lie
/// on a cycle, an order that puts each type after the types it refers to, and how each type's values arrive. Computed once,
/// so that every reader of the document asks the same graph rather than walking the types again.
/// </summary>
/// <remarks>
/// <para>
/// A type is a tree (<see cref="IrTypeTree"/>); references join the trees into a graph, which has a cycle wherever a type
/// refers to itself, directly or through others (a tree a function builds by calling itself). A walk that follows references
/// without knowing the cycles never ends, which is why the graph is computed once, here, with the cycles found.
/// </para>
/// <para>
/// The cycles are the graph's strongly connected components, found with Tarjan's algorithm (Robert Tarjan, "Depth-first
/// search and linear graph algorithms", 1972), the algorithm the inference uses for functions that call each other. The same
/// depth-first search gives the order: a type is finished after every type it reaches, so the order in which types finish
/// puts each after the types it refers to, except within a cycle, where no order can. The search starts from the results, in
/// the order the IR file lists the objects, so that the order follows the document rather than the alphabet. The guide
/// explains the graph: <c>docs/internals/11-emitting-code.md</c>.
/// </para>
/// </remarks>
public sealed class IrTypeGraph
{
    private readonly Dictionary<string, IReadOnlyList<string>> references;
    private readonly Dictionary<string, int> component;
    private readonly HashSet<int> cycles;
    private readonly Dictionary<string, IReadOnlySet<IrEncoding>> encodings;

    private IrTypeGraph(
        Dictionary<string, IReadOnlyList<string>> references,
        IReadOnlyList<string> dependenciesFirst,
        Dictionary<string, int> component,
        HashSet<int> cycles,
        Dictionary<string, IReadOnlySet<IrEncoding>> encodings)
    {
        this.references = references;
        DependenciesFirst = dependenciesFirst;
        this.component = component;
        this.cycles = cycles;
        this.encodings = encodings;
    }

    /// <summary>
    /// Every named type, each after the types it refers to unless they share a cycle: first those the results reach, in the
    /// order a depth-first search from the results finishes them, then the rest by name.
    /// </summary>
    public IReadOnlyList<string> DependenciesFirst { get; }

    /// <summary>The graph of <paramref name="document"/>'s named types.</summary>
    /// <exception cref="ArgumentException">A reference names no type in the document.</exception>
    public static IrTypeGraph Of(IrDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var names = document.Types.Keys.Order(StringComparer.Ordinal).ToList();
        var references = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            references[name] = [.. document.Types[name].Fields.SelectMany(f => IrTypeTree.References(f.Type)).Distinct(StringComparer.Ordinal)];
            foreach (var target in references[name].Where(t => !document.Types.ContainsKey(t)))
                throw new ArgumentException($"The type {name} refers to {target}, which names no type in the document.", nameof(document));
        }

        var results = IrDocumentOrder.Objects(document).SelectMany(o => o.Results ?? []).ToList();
        foreach (var target in results.SelectMany(r => IrTypeTree.References(r.Type)).Where(t => !document.Types.ContainsKey(t)))
            throw new ArgumentException($"A result refers to {target}, which names no type in the document.", nameof(document));

        var search = new Search(references);
        foreach (var root in results.SelectMany(r => IrTypeTree.References(r.Type)).Concat(names))
            search.Visit(root);

        return new IrTypeGraph(references, search.Finished, search.Component, search.Cycles, Encodings(document, results));
    }

    /// <summary>The named types <paramref name="name"/>'s fields refer to directly, in the order its fields meet them, each once.</summary>
    public IReadOnlyList<string> ReferencesOf(string name) => references[name];

    /// <summary>The named types reachable from <paramref name="types"/>, through references at any depth.</summary>
    public IReadOnlySet<string> ReachableFrom(IEnumerable<IrType> types)
    {
        ArgumentNullException.ThrowIfNull(types);
        var reached = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(types.SelectMany(IrTypeTree.References));
        while (pending.TryPop(out var name))
        {
            if (reached.Add(name))
            {
                foreach (var next in references[name])
                    pending.Push(next);
            }
        }

        return reached;
    }

    /// <summary>Whether <paramref name="name"/> refers to itself, directly or through other types: whether it lies on a cycle.</summary>
    public bool IsRecursive(string name) => cycles.Contains(component[name]);

    /// <summary>
    /// Whether two named types lie on one cycle: each reaches the other. A type lies on a cycle with itself exactly when it
    /// is recursive.
    /// </summary>
    public bool OnOneCycle(string first, string second) => component[first] == component[second] && IsRecursive(first);

    /// <summary>
    /// How the values of <paramref name="name"/> arrive, from the results that reach it: as rows, or as JSON. A type inside
    /// JSON text is JSON wherever the text is. Empty for a type no result reaches. A type the results reach both ways would
    /// need two readings; the front end never shares one so, and an emitter refuses it.
    /// </summary>
    public IReadOnlySet<IrEncoding> EncodingsOf(string name) => encodings.TryGetValue(name, out var found) ? found : new HashSet<IrEncoding>();

    /// <summary>The encodings each named type arrives in, following each result's encoding down, and JSON inside JSON text.</summary>
    private static Dictionary<string, IReadOnlySet<IrEncoding>> Encodings(IrDocument document, IReadOnlyList<IrResult> results)
    {
        var found = new Dictionary<string, HashSet<IrEncoding>>(StringComparer.Ordinal);
        var pending = new Stack<(IrType Type, IrEncoding Encoding)>(results.Select(r => (r.Type, r.Encoding)));
        while (pending.TryPop(out var next))
        {
            if (next.Type is IrRef reference)
            {
                if (!found.TryGetValue(reference.Name, out var set))
                    found[reference.Name] = set = [];
                if (set.Add(next.Encoding))
                {
                    foreach (var field in document.Types[reference.Name].Fields)
                        pending.Push((field.Type, next.Encoding));
                }

                continue;
            }

            foreach (var child in IrTypeTree.Children(next.Type))
                pending.Push((child.Type, child.Segment is IrPathJson ? IrEncoding.Json : next.Encoding));
        }

        return found.ToDictionary(e => e.Key, e => (IReadOnlySet<IrEncoding>)e.Value, StringComparer.Ordinal);
    }

    /// <summary>
    /// Tarjan's depth-first search: each type's component, the components that are cycles, and the order types finish in.
    /// </summary>
    private sealed class Search(Dictionary<string, IReadOnlyList<string>> references)
    {
        private readonly Dictionary<string, int> index = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> lowLink = new(StringComparer.Ordinal);
        private readonly Stack<string> stack = new();
        private readonly HashSet<string> onStack = new(StringComparer.Ordinal);

        public List<string> Finished { get; } = [];

        public Dictionary<string, int> Component { get; } = new(StringComparer.Ordinal);

        public HashSet<int> Cycles { get; } = [];

        public void Visit(string name)
        {
            if (index.ContainsKey(name))
                return;

            var order = index.Count;
            index[name] = order;
            lowLink[name] = order;
            stack.Push(name);
            onStack.Add(name);
            foreach (var next in references[name])
            {
                if (!index.TryGetValue(next, out var visited))
                {
                    Visit(next);
                    lowLink[name] = Math.Min(lowLink[name], lowLink[next]);
                }
                else if (onStack.Contains(next))
                {
                    lowLink[name] = Math.Min(lowLink[name], visited);
                }
            }

            Finished.Add(name);
            if (lowLink[name] != index[name])
                return;

            // The root of a component: everything above it on the stack is its component.
            var id = index[name];
            var size = 0;
            string member;
            do
            {
                member = stack.Pop();
                onStack.Remove(member);
                Component[member] = id;
                size++;
            }
            while (member != name);

            if (size > 1 || references[name].Contains(name, StringComparer.Ordinal))
                Cycles.Add(id);
        }
    }
}
