using Haruspex.Collections;
using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Model;

/// <summary>
/// An XML schema collection (<c>CREATE XML SCHEMA COLLECTION</c>): the XML Schema documents that a column, variable or parameter
/// of <c>xml(collection)</c> is validated against, which SQL Server enforces on every write. Nothing is typed from it yet;
/// it is held so that what is bound to it can be typed by its schemas later.
/// Why: docs/decisions/0133-an-xml-value-is-a-kind-of-its-own-a-branded-string.md.
/// </summary>
/// <param name="Name">Its name.</param>
/// <param name="Span">Where it is created.</param>
/// <param name="Schemas">
/// The text of each schema document, as given: the one it is created with, then each one <c>ALTER XML SCHEMA COLLECTION … ADD</c>
/// adds, in the order read. Empty when the text is not a literal, which a script may give as a variable.
/// </param>
#pragma warning disable CA1711 // SQL Server's name for the object, which is no .NET collection.
public sealed record XmlSchemaCollection(ObjectName Name, SourceSpan Span, EquatableArray<string> Schemas) : SchemaObject(Name, Span);
#pragma warning restore CA1711
