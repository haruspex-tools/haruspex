using Haruspex.Collections;
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
/// <param name="nodes">The object each recursive function the procedures call stands for, for the named type of its node.</param>
/// <param name="descriptions">The database's descriptions, of each procedure and its parameters.</param>
internal sealed class IrBuilder(IReadOnlyDictionary<ObjectName, ObjectShape> nodes, Descriptions descriptions)
{
    private readonly Dictionary<string, IrObjectType> types = new(StringComparer.Ordinal);

    /// <summary>Every type key given out, reserved before the type is written so that its fields can refer to it.</summary>
    private readonly HashSet<string> reserved = new(StringComparer.Ordinal);
    private readonly List<IrUnknown> unknowns = [];

    /// <summary>The table types the parameters take, by name, and the key given to each one's row, once its type is written.</summary>
    private readonly Dictionary<string, IrTableType> tableTypes = new(StringComparer.Ordinal);
    private readonly Dictionary<ObjectName, string> rowNames = [];

    /// <summary>The key given to each recursive function's node, once its type is written.</summary>
    private readonly Dictionary<ObjectName, string> nodeNames = [];

    /// <summary>The unknowns inside each node, with paths from the node, and the nodes each refers to.</summary>
    private readonly Dictionary<string, List<(string Path, IrUnknownReason Reason, string Detail, string At)>> nodeUnknowns = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> nodeEdges = new(StringComparer.Ordinal);

    /// <summary>The nodes being written, innermost on top.</summary>
    private readonly Stack<string> enclosingNodes = new();

    /// <summary>Which object has had which node's unknowns listed.</summary>
    private readonly HashSet<(string Object, string Node)> listedNodes = [];

    /// <summary>The document of the procedures and query files analysed, under the callers' session.</summary>
    public IrDocument Build(IReadOnlyList<(Procedure Source, ProcedureAnalysis Analysis)> procedures, IReadOnlyList<(QueryFile Source, ProcedureAnalysis Analysis)> queries, SessionOptions session)
    {
        var analysed = procedures.Select(p => (Id: p.Source.Name.ToString(), Kind: IrObjectKind.Procedure, Owner: IrTypeOwner.Procedure, p.Source.Span, Text: (string?)null, p.Analysis, Name: (ObjectName?)p.Source.Name))
            .Concat(queries.Select(q => (Id: q.Source.Path, Kind: IrObjectKind.Query, Owner: IrTypeOwner.Query, q.Source.Span, Text: (string?)q.Source.Text, q.Analysis, Name: (ObjectName?)null)));
        var ordered = analysed.OrderBy(o => o.Id, StringComparer.Ordinal).ToList();

        var objects = new List<IrObject>();
        foreach (var (id, kind, ownerKind, at, text, analysis, name) in ordered)
        {
            var owner = new Owner(ownerKind, id);

            var parameters = analysis.Parameters
                .Select(p => new IrParameter(p.Name, Convert(p.Type, owner, id, $"parameters.{p.Name}"), p.HasDefault, p.IsOutput)
                {
                    TableType = p.TableType?.Key,
                    Description = name is { } procedure ? descriptions.Of(procedure, p.Name) : null,
                })
                .ToList();
            foreach (var (key, definition) in analysis.Parameters.Select(p => p.TableType).OfType<(string, IrTableType)>())
                tableTypes.TryAdd(key, definition);

            List<IrResult>? results = null;
            if (analysis.Results is { } shapes)
            {
                results = [];
                for (var i = 0; i < shapes.Count; i++)
                    results.Add(new IrResult(shapes[i].Encoding, Convert(shapes[i].Shape, owner, id, $"results[{i}]"), shapes[i].AbsentBecause) { Cardinality = shapes[i].Cardinality });
            }
            else
            {
                // Never null: an analysis without results says why (ProcedureCalls.Analyse).
                var why = analysis.ResultsUnknown!;
                unknowns.Add(new IrUnknown(id, "results", why.Reason, why.Detail, why.At.ToString()));
            }

            objects.Add(new IrObject(id, id, kind, at.ToString(), [.. parameters], results is null ? null : [.. results])
            {
                Text = text,
                ReturnsCode = analysis.ReturnsCode,
                Description = name is { } procedure ? descriptions.Of(procedure) : null,
            });
        }

        return MarkRecursive(new IrDocument(new IrSource("tsql") { Session = Session(session) }, [.. objects], types, [.. unknowns])
        {
            TableTypes = tableTypes.ToEquatableMap(t => t.Key, t => t.Value),
        });
    }

    /// <summary>
    /// The options declared, as the IR records them: by the names the config file gives them, sorted, each left out when it is
    /// not known, so a document that assumes nothing records nothing.
    /// </summary>
    private static EquatableArray<IrSessionOption> Session(SessionOptions session) =>
        [.. session.ByName.Where(o => o.On.HasValue).Select(o => new IrSessionOption(o.Name, o.On.GetValueOrDefault())).OrderBy(o => o.Name, StringComparer.Ordinal)];

    /// <summary>The object, procedure or recursive function, that the types being written belong to.</summary>
    private sealed record Owner(IrTypeOwner Kind, string Object);

    private IrType Convert(Shape shape, Owner owner, string id, string path) => shape switch
    {
        ScalarShape scalar => scalar.Type,

        // An unknown content claims nothing more than "JSON", which the node already says; the unknown is reported where
        // json_query makes the text live, if it does.
        JsonTextShape text => new IrJsonText(text.Content is null or UnknownShape ? null : Convert(text.Content, owner, id, path + "<json>")),
        AnyJsonShape any => new IrAnyJson(any.ObjectOrArray),
        RecursiveShape recursive => Node(recursive.Function, id),
        TableRowShape row => Row(row),
        NeverShape never => Reported(id, path, IrUnknownReason.UnresolvedRecursion, "The function called here never returns a value: every return calls itself again.", never.At.ToString()),
        ArrayShape array => Array(array, Convert(array.Items, owner, id, path + "[]")),

        // Each position is its own path, so an unknown element is reported where it is.
        TupleShape tuple => new IrTuple([.. tuple.Items.Select((e, i) => new IrElement(
            Convert(e.Type, owner, id, path + "[" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]"),
            e.NullableBecause))]),

        // A record's values have no key to name them by; {} stands for any key.
        RecordShape record => new IrRecord(Convert(record.Values, owner, id, path + "{}"), record.ValuesNullableBecause),
        UnionShape union => new IrUnion([.. union.Variants.Select(v => Convert(v, owner, id, path))]),
        ObjectShape obj => Object(obj, owner, id, path),
        UnknownShape unknown => Reported(id, path, unknown.Reason, unknown.Detail, unknown.At.ToString()),
    };

    /// <summary>An unknown in the IR, reported with why and where.</summary>
    private IrUnknownType Reported(string id, string path, IrUnknownReason reason, string detail, string at)
    {
        Report(id, path, reason, detail, at);
        return new IrUnknownType(reason);
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
                Object(nodes[function], owner, id, path, name);
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

    /// <summary>
    /// A table type's row: one named type for the whole document, owned by the type and shared by every parameter of it, written
    /// the first time it is met. Its columns are all known (an unknown one makes the parameter unknown: <see cref="TableTypes"/>),
    /// so it has no unknowns to list.
    /// </summary>
    private IrRef Row(TableRowShape row)
    {
        if (!rowNames.TryGetValue(row.Type, out var key))
        {
            var owner = new Owner(IrTypeOwner.TableType, row.Type.ToString());
            key = Object(row.Row, owner, owner.Object, $"<{row.Type}>").Name;
            rowNames[row.Type] = key;
        }

        return new IrRef(key);
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
    private void Report(string id, string path, IrUnknownReason reason, string detail, string at)
    {
        if (enclosingNodes.TryPeek(out var node))
            nodeUnknowns[node].Add((path, reason, detail, at));
        else
            unknowns.Add(new IrUnknown(id, path, reason, detail, at));
    }

    /// <summary>
    /// The document with every object type that can reach itself through references marked, so that readers which expand
    /// references know where to stop. The cycles are the type graph's (<see cref="IrTypeGraph"/>).
    /// </summary>
    private static IrDocument MarkRecursive(IrDocument document)
    {
        var graph = IrTypeGraph.Of(document);
        return document with
        {
            Types = document.Types.ToDictionary(t => t.Key, t => t.Value with { Recursive = graph.IsRecursive(t.Key) }, StringComparer.Ordinal),
        };
    }

    private static IrArray Array(ArrayShape array, IrType items) =>
        new(items) { ItemsNullableBecause = array.ItemsNullableBecause, MinItems = array.MinItems, MaxItems = array.MaxItems };

    /// <param name="key">The type's key, already reserved, or null to reserve one for its identity.</param>
    private IrRef Object(ObjectShape obj, Owner owner, string id, string path, string? key = null)
    {
        key ??= Key(owner, path);
        var fields = new List<IrField>();
        foreach (var field in obj.Fields)
            fields.Add(new IrField(field.Name, Convert(field.Type, owner, id, path + IrPath.Key(field.Name)), field.Presence, field.NullableBecause) { From = field.From, Description = field.Description });

        types[key] = new IrObjectType([.. fields]) { Identity = new IrTypeIdentity(owner.Kind, owner.Object, path), Open = obj.Open };
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
