namespace Haruspex.SqlServer.Semantics.XmlSchemas;

/// <summary>A node <c>nodes()</c> gave from a typed instance, of the declaration its path reached. It is there: it is a row.</summary>
/// <param name="Node">The node's declaration.</param>
public sealed record XmlNodeStart(XmlNode Node) : XmlStart;
