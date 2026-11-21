namespace Haruspex.Ir;

/// <summary>
/// The structure of one type, without following its references: the types directly inside each node, every node within it,
/// and a copy with its references renamed. The one place that knows which node holds which, so that a walk over a type
/// can't miss a kind of node. A walk that reaches a node it doesn't know fails rather than skips it.
/// </summary>
/// <remarks>
/// What lies behind a reference is the document's (<see cref="IrTypeGraph"/>): a type alone is a tree, and the references
/// that join its trees into a graph, possibly with cycles, are followed only there. The guide explains why the walks were
/// gathered here: <c>docs/internals/11-emitting-code.md</c>.
/// </remarks>
public static class IrTypeTree
{
    /// <summary>The types directly inside <paramref name="type"/>, in the order the IR lists them, each with its path segment.</summary>
    public static IEnumerable<IrTypeChild> Children(IrType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type switch
        {
            IrScalar or IrRef or IrImport or IrUnknownType or IrAnyJson => [],
            IrArray array => [new IrTypeChild(array.Items, new IrPathItems())],
            IrTuple tuple => tuple.Items.Select((element, i) => new IrTypeChild(element.Type, new IrPathIndex(i))),
            IrRecord record => [new IrTypeChild(record.Values, new IrPathValues())],
            IrUnion union => union.Variants.Select(variant => new IrTypeChild(variant, null)),
            IrJsonText text => text.Content is { } content ? [new IrTypeChild(content, new IrPathJson())] : [],
        };
    }

    /// <summary>
    /// <paramref name="type"/> and every type inside it, each before the types inside it and in the IR's order, without
    /// following references: a reference is a node here, and what it names is not.
    /// </summary>
    public static IEnumerable<IrType> Within(IrType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var pending = new Stack<IrType>([type]);
        while (pending.TryPop(out var next))
        {
            yield return next;
            foreach (var child in Children(next).Reverse())
                pending.Push(child.Type);
        }
    }

    /// <summary>The names of the object types <paramref name="type"/> refers to, in the order <see cref="Within"/> meets them, each once.</summary>
    public static IEnumerable<string> References(IrType type) =>
        Within(type).OfType<IrRef>().Select(reference => reference.Name).Distinct(StringComparer.Ordinal);

    /// <summary>
    /// A copy of <paramref name="type"/> whose every reference names <paramref name="rename"/>'s name for it, and which is
    /// otherwise the same: each node keeps its nullability, lengths and facets.
    /// </summary>
    public static IrType RenameReferences(IrType type, Func<string, string> rename)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(rename);
        IrType Renamed(IrType inner) => RenameReferences(inner, rename);
        return type switch
        {
            IrRef reference => new IrRef(rename(reference.Name)),
            IrScalar or IrImport or IrUnknownType or IrAnyJson => type,
            IrArray array => array with { Items = Renamed(array.Items) },
            IrTuple tuple => new IrTuple([.. tuple.Items.Select(e => e with { Type = Renamed(e.Type) })]),
            IrRecord record => record with { Values = Renamed(record.Values) },
            IrUnion union => new IrUnion([.. union.Variants.Select(Renamed)]),
            IrJsonText text => text.Content is { } content ? new IrJsonText(Renamed(content)) : type,
        };
    }
}
