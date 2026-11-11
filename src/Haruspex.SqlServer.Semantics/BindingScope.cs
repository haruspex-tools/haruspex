namespace Haruspex.SqlServer.Semantics;

/// <summary>The sources a query's FROM clause binds, with the enclosing query's scope for correlated subqueries.</summary>
public sealed class BindingScope
{
    private readonly List<BoundSource> sources = [];

    internal BindingScope(BindingScope? parent, ILocalRelations? locals)
    {
        Parent = parent;
        Locals = locals ?? parent?.Locals;
    }

    /// <summary>The relations the body defines that names in this scope may refer to; inherited by nested scopes.</summary>
    public ILocalRelations? Locals { get; }

    /// <summary>The enclosing query's scope, or null for an outermost query.</summary>
    public BindingScope? Parent { get; }

    /// <summary>The sources, in the order they appear.</summary>
    public IReadOnlyList<BoundSource> Sources => sources;

    internal void Add(BoundSource source) => sources.Add(source);

    /// <summary>
    /// The scope an <c>APPLY</c>'s right side reads its outer columns from: the sources of the APPLY's left input, from
    /// <paramref name="first"/> on, as the same objects, with this scope's parent beyond them.
    /// </summary>
    /// <param name="first">The position of the left input's first source.</param>
    /// <param name="outer">The scope an enclosing APPLY's right side reads, when this APPLY is inside one; else the parent.</param>
    internal BindingScope Lateral(int first, BindingScope? outer)
    {
        var lateral = new BindingScope(outer ?? Parent, Locals);
        lateral.sources.AddRange(sources.Skip(first));
        return lateral;
    }
}
