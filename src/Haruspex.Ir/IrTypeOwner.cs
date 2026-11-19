namespace Haruspex.Ir;

/// <summary>What kind of database object a type belongs to.</summary>
public enum IrTypeOwner
{
    /// <summary>A procedure in the document's objects: the type is part of its results.</summary>
    Procedure,

    /// <summary>
    /// A scalar function that calls itself: the type is its node, or inside it, and is shared by every object whose
    /// results reach it.
    /// </summary>
    Function,

    /// <summary>A query file in the document's objects (0.22): the type is part of its results.</summary>
    Query,

    /// <summary>
    /// A table type in the document's <see cref="IrDocument.TableTypes"/> (0.26): the type is a row of it, as a client sends one in
    /// a table-valued parameter, shared by every parameter of the type.
    /// </summary>
    TableType,
}
