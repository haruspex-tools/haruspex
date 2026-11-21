using Haruspex.Collections;

namespace Haruspex.Ir;

/// <summary>A named object type.</summary>
/// <param name="Fields">Its fields, in order.</param>
public sealed record IrObjectType(EquatableArray<IrField> Fields)
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

    /// <summary>
    /// Whether the config file gave the type its name (a <c>name</c> in <c>paths</c>) rather than the naming rules. An emitter that
    /// declares a shape several types share once declares it under a declared name when one of them has one
    /// (<c>docs/decisions/0071-a-shared-shape-is-named-by-declaration-or-by-its-structure.md</c>).
    /// </summary>
    public bool NameDeclared { get; init; }

    /// <summary>
    /// Whether its values may hold keys it doesn't list, each of any JSON value (0.28): an object in JSON a column is declared to
    /// hold, whose schema doesn't close it with <c>additionalProperties: false</c>, since whoever wrote it may have added others.
    /// An object the database builds holds only its fields' keys. An emitter must accept such keys, or set them aside, and never
    /// refuse a value for one: not under a strict objects policy, and not to tell a union's variants apart
    /// (docs/decisions/0093-a-column-s-json-is-part-of-its-type-completed-by-the-config.md).
    /// </summary>
    public bool Open { get; init; }
}
