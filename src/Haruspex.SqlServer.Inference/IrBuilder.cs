using Haruspex.Ir;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// Turns inferred shapes into an <see cref="IrDocument"/>: gives every object type its identity (the object and the path
/// it belongs to), and lists every unknown with its path.
/// </summary>
/// <remarks>
/// Names for generated code are not inference's concern: they come from the config file's naming rules and overrides,
/// applied by Haruspex.Config to the document this builds. So each procedure's name is left as its id, and each type is
/// keyed by its identity (<c>sales.get_order:results[0][]</c>) until then. Settling names here, with a number added to
/// break a clash, would let adding one procedure rename another's types.
/// </remarks>
/// <param name="functions">The functions the procedures call, for the named type of each recursive one's node.</param>
internal sealed class IrBuilder(FunctionSummaries functions)
{
    private readonly Dictionary<string, IrObjectType> types = new(StringComparer.Ordinal);

    /// <summary>Every type key given out, reserved before the type is written so that its fields can refer to it.</summary>
    private readonly HashSet<string> reserved = new(StringComparer.Ordinal);
    private readonly List<IrUnknown> unknowns = [];

    /// <summary>The key given to each recursive function's node, once its type is written.</summary>
    private readonly Dictionary<ObjectName, string> nodeNames = [];

    /// <summary>The unknowns inside each node, with paths from the node, and the nodes each refers to.</summary>
    private readonly Dictionary<string, List<(string Path, string Reason, string Detail, string At)>> nodeUnknowns = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> nodeEdges = new(StringComparer.Ordinal);

    /// <summary>The nodes being written, innermost on top.</summary>
    private readonly Stack<string> enclosingNodes = new();

    /// <summary>Which object has had which node's unknowns listed.</summary>
    private readonly HashSet<(string Object, string Node)> listedNodes = [];

    public IrDocument Build(IReadOnlyList<(Procedure Source, ProcedureAnalysis Analysis)> procedures)
    {
        var ordered = procedures.OrderBy(p => p.Source.Name.ToString(), StringComparer.Ordinal).ToList();

        var objects = new List<IrObject>();
        foreach (var (source, analysis) in ordered)
        {
            var id = source.Name.ToString();
            var owner = new Owner(IrTypeOwner.Procedure, id);

            var parameters = analysis.Parameters
                .Select(p => new IrParameter(p.Name, Convert(p.Type, owner, id, $"parameters.{p.Name}"), p.HasDefault, p.IsOutput))
                .ToList();

            List<IrResult>? results = null;
            if (analysis.Results is { } shapes)
            {
                results = [];
                for (var i = 0; i < shapes.Count; i++)
                    results.Add(new IrResult(shapes[i].Encoding, Convert(shapes[i].Shape, owner, id, $"results[{i}]"), shapes[i].AbsentBecause) { Cardinality = shapes[i].Cardinality });
            }
            else
            {
                var why = analysis.ResultsUnknown!;
                unknowns.Add(new IrUnknown(id, "results", why.Reason, why.Detail, why.At.ToString()));
            }

            var at = source.At;
            objects.Add(new IrObject(id, id, IrObjectKind.Procedure, at.ToString(), parameters, results));
        }

        MarkRecursive();
        return new IrDocument(new IrSource("tsql"), objects, types, unknowns);
    }

    /// <summary>The object, procedure or recursive function, that the types being written belong to.</summary>
    private sealed record Owner(IrTypeOwner Kind, string Object);

    private IrType Convert(Shape shape, Owner owner, string id, string path)
    {
        switch (shape)
        {
            case ScalarShape scalar:
                return scalar.Type;
            case JsonTextShape text:
                // An unknown content claims nothing more than "JSON", which the node already says; the unknown is
                // reported where json_query makes the text live, if it does.
                return new IrJsonText(text.Content is null or UnknownShape ? null : Convert(text.Content, owner, id, path + "<json>"));
            case RecursiveShape recursive:
                return Node(recursive.Function, id);
            case NeverShape never:
                Report(id, path, IrUnknownReasons.UnresolvedRecursion, "The function called here never returns a value: every return calls itself again.", never.At.ToString());
                return new IrUnknownType(IrUnknownReasons.UnresolvedRecursion);
            case ArrayShape array:
                return Array(array, Convert(array.Items, owner, id, path + "[]"));
            case TupleShape tuple:
                // Each position is its own path, so an unknown element is reported where it is.
                return new IrTuple([.. tuple.Items.Select((e, i) => new IrElement(
                    Convert(e.Type, owner, id, path + "[" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]"),
                    e.NullableBecause))]);
            case RecordShape record:
                // A record's values have no key to name them by; {} stands for any key.
                return new IrRecord(Convert(record.Values, owner, id, path + "{}"), record.ValuesNullableBecause);
            case UnionShape union:
                return new IrUnion([.. union.Variants.Select(v => Convert(v, owner, id, path))]);
            case ObjectShape obj:
                return Object(obj, owner, id, path);
            case UnknownShape unknown:
                Report(id, path, unknown.Reason, unknown.Detail, unknown.At.ToString());
                return new IrUnknownType(unknown.Reason);
            default:
                throw new ArgumentOutOfRangeException(nameof(shape), shape, "Unknown shape.");
        }
    }

    /// <summary>
    /// A recursive function's node: one named type for the whole document, written the first time it is met, with its
    /// key reserved before its fields are converted so that the fields can refer to it.
    /// </summary>
    /// <remarks>
    /// The node is shared by every object that reaches it, so an unknown inside it is reported with a path that starts at
    /// the node, named by the function (<c>&lt;bom.components&gt;.part.label</c>), and listed under every object whose
    /// results reach the node, directly or through other nodes.
    /// </remarks>
    private IrRef Node(ObjectName function, string id)
    {
        if (!nodeNames.TryGetValue(function, out var name))
        {
            var owner = new Owner(IrTypeOwner.Function, function.ToString());
            var path = $"<{function}>";
            name = Key(owner, path);
            nodeNames[function] = name;
            nodeUnknowns[name] = [];
            nodeEdges[name] = [];
            if (enclosingNodes.TryPeek(out var outer))
                nodeEdges[outer].Add(name);
            enclosingNodes.Push(name);
            try
            {
                Object(functions.Nodes[function], owner, id, path, name);
            }
            finally
            {
                enclosingNodes.Pop();
            }
        }
        else if (enclosingNodes.TryPeek(out var outer))
        {
            nodeEdges[outer].Add(name);
        }

        if (enclosingNodes.Count == 0)
            ListNodeUnknowns(id, name);
        return new IrRef(name);
    }

    /// <summary>Lists, under the object <paramref name="id"/>, the unknowns of a node and of every node it reaches.</summary>
    private void ListNodeUnknowns(string id, string node)
    {
        var queue = new Queue<string>([node]);
        while (queue.TryDequeue(out var next))
        {
            if (!listedNodes.Add((id, next)))
                continue;
            foreach (var (path, reason, detail, at) in nodeUnknowns[next])
                unknowns.Add(new IrUnknown(id, path, reason, detail, at));
            foreach (var further in nodeEdges[next])
                queue.Enqueue(further);
        }
    }

    /// <summary>An unknown at <paramref name="path"/>: kept with the node being written, if any, to list under each object that reaches it.</summary>
    private void Report(string id, string path, string reason, string detail, string at)
    {
        if (enclosingNodes.TryPeek(out var node))
            nodeUnknowns[node].Add((path, reason, detail, at));
        else
            unknowns.Add(new IrUnknown(id, path, reason, detail, at));
    }

    /// <summary>
    /// Marks every object type that can reach itself through references (inside arrays, tuples, records, unions and the
    /// content of JSON text too), so that readers which expand references know where to stop.
    /// </summary>
    private void MarkRecursive()
    {
        var edges = types.ToDictionary(t => t.Key, t => t.Value.Fields.SelectMany(f => Refs(f.Type)).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var name in types.Keys.ToList())
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<string>(edges[name]);
            while (queue.TryDequeue(out var next))
            {
                if (!seen.Add(next))
                    continue;
                foreach (var further in edges[next])
                    queue.Enqueue(further);
            }

            if (seen.Contains(name))
                types[name] = types[name] with { Recursive = true };
        }
    }

    private static IEnumerable<string> Refs(IrType type) => type switch
    {
        IrRef reference => [reference.Name],
        IrArray array => Refs(array.Items),
        IrTuple tuple => tuple.Items.SelectMany(e => Refs(e.Type)),
        IrRecord record => Refs(record.Values),
        IrUnion union => union.Variants.SelectMany(Refs),
        IrJsonText { Content: { } content } => Refs(content),
        _ => [],
    };

    private static IrArray Array(ArrayShape array, IrType items) =>
        new(items) { ItemsNullableBecause = array.ItemsNullableBecause ?? [], MinItems = array.MinItems, MaxItems = array.MaxItems };

    /// <param name="key">The type's key, already reserved, or null to reserve one for its identity.</param>
    private IrRef Object(ObjectShape obj, Owner owner, string id, string path, string? key = null)
    {
        key ??= Key(owner, path);
        var fields = new List<IrField>();
        foreach (var field in obj.Fields)
            fields.Add(new IrField(field.Name, Convert(field.Type, owner, id, path + IrPath.Key(field.Name)), field.Presence, field.NullableBecause));

        types[key] = new IrObjectType(fields) { Identity = new IrTypeIdentity(owner.Kind, owner.Object, path) };
        return new IrRef(key);
    }

    /// <summary>
    /// A provisional key for a type, from its identity, reserved at once. The variants of a union share a path, so a
    /// later one gets a number; the key is replaced by a name when names are settled, so the number never reaches
    /// generated code.
    /// </summary>
    private string Key(Owner owner, string path)
    {
        var key = $"{owner.Object}:{path}";
        var candidate = key;
        for (var n = 2; !reserved.Add(candidate); n++)
            candidate = key + "~" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return candidate;
    }
}
