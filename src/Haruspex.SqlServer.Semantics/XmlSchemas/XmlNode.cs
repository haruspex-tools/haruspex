using Haruspex.SqlServer.Model.XmlSchemas;

namespace Haruspex.SqlServer.Semantics.XmlSchemas;

/// <summary>The declaration of the nodes a path reaches: an element or an attribute, and its type.</summary>
/// <param name="Type">The node's type: an attribute's simple type, or an element's.</param>
/// <param name="IsAttribute">Whether it is an attribute.</param>
/// <param name="Nillable">Whether an element may be <c>xsi:nil</c>, which <c>value()</c> reads as NULL (observed); false for an attribute.</param>
/// <param name="Declaration">The path to it, as a fact's source names it: <c>{urn:shop}order/@id</c>.</param>
public sealed record XmlNode(XmlTypeDeclaration Type, bool IsAttribute, bool Nillable, string Declaration)
{
    /// <summary>
    /// Whether <c>value()</c> reads a value from every such node, never NULL: an attribute, or an element that isn't nillable, of a
    /// simple type or of simple content whose type an instance can't replace. The server refuses to read an element of other
    /// content (9314: observed: Value_of_an_element_of_complex_content_is_refused).
    /// </summary>
    public bool HasValue => !Nillable && Type is XmlSimpleTypeDeclaration or XmlComplexTypeDeclaration { Content: XmlContentKind.Simple, Replaceable: false };

    /// <summary>Its simple type, whose facets hold of its value; null for an element of complex type, simple content included.</summary>
    public XmlSimpleTypeDeclaration? SimpleType => Type as XmlSimpleTypeDeclaration;
}
