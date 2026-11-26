using Haruspex.Collections;

namespace Haruspex.SqlServer.Model.XmlSchemas;

/// <summary>A complex type: its content, its attributes, and whether an instance may replace it.</summary>
/// <param name="Name">Its name; null for an anonymous type.</param>
/// <param name="Content">What its content holds.</param>
/// <param name="Particle">Its content model, with every group and reference resolved; null when it holds no elements.</param>
/// <param name="Attributes">Its attributes, its base types' included.</param>
/// <param name="Replaceable">
/// Whether an instance may give an element of it another type: it is <c>xs:anyType</c>, abstract, or the collection derives
/// another type from it, which <c>xsi:type</c> may name.
/// </param>
public sealed record XmlComplexTypeDeclaration(XmlName? Name, XmlContentKind Content, XmlParticle? Particle, EquatableArray<XmlAttributeDeclaration> Attributes, bool Replaceable)
    : XmlTypeDeclaration(Name);
