using System.Text.Encodings.Web;
using System.Text.Json;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// Turns inferred shapes into an <see cref="IrDocument"/>: settles a name for every procedure and object type
/// (names are decided once, here, so every emitter agrees), and lists every unknown with its path.
/// </summary>
/// <param name="functions">The functions the procedures call, for the named type of each recursive one's node.</param>
internal sealed class IrBuilder(FunctionSummaries functions)
{
    private readonly Dictionary<string, IrObjectType> types = new(StringComparer.Ordinal);
    private readonly HashSet<string> names = new(StringComparer.Ordinal);
    private readonly List<IrUnknown> unknowns = [];

    /// <summary>The name given to each recursive function's node, once its type is written.</summary>
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
        var procedureNames = ProcedureNames(ordered.Select(p => p.Source.Name).ToList());

        // Procedure names are taken first, so that a type can never take a procedure's name.
        foreach (var name in procedureNames.Values)
            names.Add(name);

        var objects = new List<IrObject>();
        foreach (var (source, analysis) in ordered)
        {
            var id = source.Name.ToString();
            var name = procedureNames[source.Name];

            var parameters = analysis.Parameters
                .Select(p => new IrParameter(p.Name, Convert(p.Type, "", id, $"parameters.{p.Name}"), p.HasDefault, p.IsOutput))
                .ToList();

            List<IrResult>? results = null;
            if (analysis.Results is { } shapes)
            {
                results = [];
                for (var i = 0; i < shapes.Count; i++)
                {
                    // The procedure's name is already reserved; a second result's base name must be reserved here.
                    var root = shapes.Count == 1 ? name : Unique($"{name}Result{i + 1}");
                    results.Add(new IrResult(shapes[i].Encoding, Root(shapes[i].Shape, root, id, $"results[{i}]"), shapes[i].AbsentBecause));
                }
            }
            else
            {
                var why = analysis.ResultsUnknown!;
                unknowns.Add(new IrUnknown(id, "results", why.Reason, why.Detail, why.At.ToString()));
            }

            var at = source.At;
            objects.Add(new IrObject(id, name, IrObjectKind.Procedure, at.ToString(), parameters, results));
        }

        MarkRecursive();
        return new IrDocument(new IrSource("tsql"), objects, types, unknowns);
    }

    /// <summary>
    /// A result's root type. Its object is the procedure's name plus <c>Row</c>, whether it is one of a list
    /// or the single object of <c>WITHOUT_ARRAY_WRAPPER</c>; the procedure's own name is kept for the whole
    /// result, which may be null. Nested names build on the procedure's name.
    /// </summary>
    private IrType Root(Shape shape, string name, string id, string path) => shape switch
    {
        ObjectShape obj => Object(obj, Unique(name + "Row"), name, id, path),
        ArrayShape { Items: ObjectShape row } array => Array(array, Object(row, Unique(name + "Row"), name, id, path + "[]")),
        _ => Convert(shape, name, id, path),
    };

    private IrType Convert(Shape shape, string objectName, string id, string path)
    {
        switch (shape)
        {
            case ScalarShape scalar:
                return scalar.Type;
            case JsonTextShape text:
                // An unknown content claims nothing more than "JSON", which the node already says; the unknown is
                // reported where json_query makes the text live, if it does.
                return new IrJsonText(text.Content is null or UnknownShape ? null : Convert(text.Content, objectName, id, path + "<json>"));
            case RecursiveShape recursive:
                return Node(recursive.Function, id);
            case NeverShape never:
                Report(id, path, IrUnknownReasons.UnresolvedRecursion, "The function called here never returns a value: every return calls itself again.", never.At.ToString());
                return new IrUnknownType(IrUnknownReasons.UnresolvedRecursion);
            case ArrayShape array:
                return Array(array, Convert(array.Items, objectName, id, path + "[]"));
            case TupleShape tuple:
                // Each position is its own path, so an unknown element is reported where it is.
                return new IrTuple([.. tuple.Items.Select((e, i) => new IrElement(
                    Convert(e.Type, objectName, id, path + "[" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]"),
                    e.NullableBecause))]);
            case RecordShape record:
                // A record's values have no key to name them by; {} stands for any key.
                return new IrRecord(Convert(record.Values, objectName, id, path + "{}"), record.ValuesNullableBecause);
            case UnionShape union:
                return new IrUnion([.. union.Variants.Select(v => Convert(v, objectName, id, path))]);
            case ObjectShape obj:
                var name = Unique(objectName);
                return Object(obj, name, name, id, path);
            case UnknownShape unknown:
                Report(id, path, unknown.Reason, unknown.Detail, unknown.At.ToString());
                return new IrUnknownType(unknown.Reason);
            default:
                throw new ArgumentOutOfRangeException(nameof(shape), shape, "Unknown shape.");
        }
    }

    /// <summary>
    /// A recursive function's node: one named type for the whole document, written the first time it is met, with its
    /// name reserved before its fields are converted so that the fields can refer to it.
    /// </summary>
    /// <remarks>
    /// The node is shared by every object that reaches it, so an unknown inside it is reported with a path that starts at
    /// the node (<c>&lt;ComponentsNode&gt;.part.label</c>) and listed under every object whose results reach the node,
    /// directly or through other nodes.
    /// </remarks>
    private IrRef Node(ObjectName function, string id)
    {
        if (!nodeNames.TryGetValue(function, out var name))
        {
            name = Unique(Naming.Pascal(function.Name) + "Node");
            nodeNames[function] = name;
            nodeUnknowns[name] = [];
            nodeEdges[name] = [];
            if (enclosingNodes.TryPeek(out var outer))
                nodeEdges[outer].Add(name);
            enclosingNodes.Push(name);
            try
            {
                Object(functions.Nodes[function], name, name, id, $"<{name}>");
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

    /// <param name="name">The type's name, already reserved.</param>
    /// <param name="childBase">What nested types' names start with.</param>
    private IrRef Object(ObjectShape obj, string name, string childBase, string id, string path)
    {
        var fields = new List<IrField>();
        foreach (var field in obj.Fields)
        {
            var pascal = Naming.Pascal(field.Name);
            var childName = childBase + (field.Type is ArrayShape or RecordShape ? Naming.Singular(pascal) : pascal);
            fields.Add(new IrField(field.Name, Convert(field.Type, childName, id, path + Segment(field.Name)), field.Presence, field.NullableBecause));
        }

        types[name] = new IrObjectType(fields);
        return new IrRef(name);
    }

    private string Unique(string name)
    {
        var candidate = name;
        for (var n = 2; !names.Add(candidate); n++)
            candidate = name + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return candidate;
    }

    /// <summary>
    /// Each procedure's name: its own name in Pascal case, with the schema in front when two schemas have a
    /// procedure of the same name.
    /// </summary>
    private static Dictionary<ObjectName, string> ProcedureNames(IReadOnlyList<ObjectName> procedures)
    {
        var result = new Dictionary<ObjectName, string>();
        foreach (var group in procedures.GroupBy(p => Naming.Pascal(p.Name), StringComparer.Ordinal))
        {
            foreach (var procedure in group)
                result[procedure] = group.Count() == 1 ? group.Key : Naming.Pascal(procedure.Schema) + group.Key;
        }

        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (var procedure in procedures)
        {
            var name = result[procedure];
            for (var n = 2; !taken.Add(result[procedure]); n++)
                result[procedure] = name + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return result;
    }

    private static string Segment(string key) =>
        IsIdentifier(key) ? "." + key : "[\"" + JsonEncodedText.Encode(key, JavaScriptEncoder.UnsafeRelaxedJsonEscaping) + "\"]";

    private static bool IsIdentifier(string key) =>
        key.Length > 0 && (char.IsAsciiLetter(key[0]) || key[0] == '_') && key.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
}
