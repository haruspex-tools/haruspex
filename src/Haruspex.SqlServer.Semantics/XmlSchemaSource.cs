using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// A fact proved by an XML schema collection: a facet of the simple type of the node a <c>value()</c> reads, which SQL Server
/// validates on every write. Why: docs/decisions/0139-xml-is-typed-from-its-schema-collection-where-the-schema-proves-it.md.
/// </summary>
/// <param name="Collection">The collection.</param>
/// <param name="Declaration">The node's declaration, by the path to it: <c>{urn:shop}order/@id</c>.</param>
public sealed record XmlSchemaSource(ObjectName Collection, string Declaration) : FactSource;
