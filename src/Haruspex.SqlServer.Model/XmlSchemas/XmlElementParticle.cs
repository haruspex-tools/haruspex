namespace Haruspex.SqlServer.Model.XmlSchemas;

/// <summary>An element in a content model.</summary>
/// <param name="Element">The element.</param>
/// <param name="MinOccurs">Its least number of occurrences.</param>
/// <param name="MaxOccurs">Its most; null for unbounded.</param>
public sealed record XmlElementParticle(XmlElementDeclaration Element, int MinOccurs, int? MaxOccurs) : XmlParticle(MinOccurs, MaxOccurs);
