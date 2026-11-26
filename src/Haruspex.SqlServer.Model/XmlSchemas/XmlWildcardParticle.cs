namespace Haruspex.SqlServer.Model.XmlSchemas;

/// <summary>An element wildcard, <c>xs:any</c>, in a content model.</summary>
/// <param name="MinOccurs">Its least number of occurrences.</param>
/// <param name="MaxOccurs">Its most; null for unbounded.</param>
public sealed record XmlWildcardParticle(int MinOccurs, int? MaxOccurs) : XmlParticle(MinOccurs, MaxOccurs);
