namespace Haruspex.SqlServer.Model.XmlSchemas;

/// <summary>A simple type: what kind of value it holds, and the facets that hold of every value through each type it restricts.</summary>
/// <param name="Name">Its name; null for an anonymous type.</param>
/// <param name="Kind">What kind of value it holds.</param>
/// <param name="Facets">The facets that hold of its values: its own, its base types', and the built-in type's own range.</param>
public sealed record XmlSimpleTypeDeclaration(XmlName? Name, XmlValueKind Kind, XmlFacets Facets) : XmlTypeDeclaration(Name);
