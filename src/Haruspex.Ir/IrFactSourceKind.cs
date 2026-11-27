namespace Haruspex.Ir;

/// <summary>What an <see cref="IrFactSource"/> is. Its name in the IR's JSON is in <see cref="IrWireNames"/>.</summary>
public enum IrFactSourceKind
{
    /// <summary>The value's type, such as <c>nvarchar(40)</c> or an alias type's name.</summary>
    Type,

    /// <summary>A check constraint.</summary>
    Check,

    /// <summary>
    /// The engine itself: a value it writes from a closed set, such as <c>$action</c> in a <c>MERGE</c>'s <c>OUTPUT</c>, or
    /// within a range it fixes, such as <c>ASCII</c>'s 0 to 255. The ref names the value.
    /// </summary>
    Engine,

    /// <summary>
    /// An expression of the procedure whose every result is a literal: a <c>CASE</c>, <c>IIF</c> or <c>CHOOSE</c> gives one of
    /// them, exactly as written, and so does a column of a set operation each of whose queries gives a literal (0.13). The ref
    /// is the expression's (or the literal's) location.
    /// </summary>
    Expression,

    /// <summary>
    /// A declaration in the config file (0.28): a JSON Schema's <c>enum</c>, <c>const</c> or <c>minLength</c>, in a column declared
    /// to hold JSON of the schema, or in a procedure's declared results. The ref names what was declared: the column
    /// (<c>schema.table.column</c>), or the procedure.
    /// </summary>
    Declared,

    /// <summary>
    /// An XML schema collection (0.35): a facet of the simple type of the node a value is read from by XML's <c>value()</c>, which
    /// SQL Server validates on every write of an instance typed by the collection, carried where the conversion to the value's
    /// type keeps it. The ref names the collection and the node's declaration, by the path to it
    /// (<c>shop.order_schema {urn:shop}order/@id</c>). Why: docs/decisions/0139-xml-is-typed-from-its-schema-collection-where-the-schema-proves-it.md.
    /// </summary>
    XmlSchema,
}
