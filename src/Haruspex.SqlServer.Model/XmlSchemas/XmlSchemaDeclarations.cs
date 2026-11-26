using Haruspex.Collections;

namespace Haruspex.SqlServer.Model.XmlSchemas;

/// <summary>
/// The declarations of an XML schema collection's schemas, compiled together, as far as a path through an instance needs them:
/// its global elements, and the types they and their content are of. SQL Server validates every typed instance against its
/// collection on every write, so what these constrain holds of every stored value. Sources reads them; Semantics interprets them.
/// Why: docs/decisions/0139-xml-is-typed-from-its-schema-collection-where-the-schema-proves-it.md.
/// </summary>
/// <param name="Globals">The global elements of every schema in the collection, any of which may be a <c>DOCUMENT</c> instance's root.</param>
/// <param name="Types">Every type an element or attribute is of, built-in ones included; an element names its type by its place here.</param>
public sealed record XmlSchemaDeclarations(EquatableArray<XmlElementDeclaration> Globals, EquatableArray<XmlTypeDeclaration> Types);
