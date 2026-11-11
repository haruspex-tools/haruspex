namespace Haruspex.Ir;

/// <summary>A named object type.</summary>
/// <param name="Fields">Its fields, in order.</param>
public sealed record IrObjectType(IReadOnlyList<IrField> Fields)
{
    /// <summary>
    /// Where the type comes from in database terms, independent of its generated name: an object and the path to the
    /// type within it. Names are generated from config and can change; this cannot. Null only in a document built by
    /// hand (a test's).
    /// </summary>
    public IrTypeIdentity? Identity { get; init; }

    /// <summary>
    /// Whether the type refers to itself, directly or through other types (a tree a function builds by calling itself).
    /// A reader that expands references must stop at a recursive type, or it never ends. Set by whoever builds the
    /// document; a reader may also find cycles itself, by following references with the names it is expanding.
    /// </summary>
    public bool Recursive { get; init; }
}
