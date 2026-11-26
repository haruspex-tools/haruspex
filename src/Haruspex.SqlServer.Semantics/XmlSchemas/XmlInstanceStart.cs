namespace Haruspex.SqlServer.Semantics.XmlSchemas;

/// <summary>A typed instance: a column, variable or parameter of <c>xml(collection)</c>.</summary>
/// <param name="Document">Whether it is <c>DOCUMENT</c>, exactly one top-level element; <c>CONTENT</c>, the default, has any number, none included.</param>
public sealed record XmlInstanceStart(bool Document) : XmlStart;
