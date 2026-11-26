using Haruspex.Collections;

namespace Haruspex.SqlServer.Model.XmlSchemas;

/// <summary>A sequence, choice or all group in a content model.</summary>
/// <param name="Kind">Which.</param>
/// <param name="Items">Its particles.</param>
/// <param name="MinOccurs">Its least number of occurrences.</param>
/// <param name="MaxOccurs">Its most; null for unbounded.</param>
public sealed record XmlGroupParticle(XmlGroupKind Kind, EquatableArray<XmlParticle> Items, int MinOccurs, int? MaxOccurs) : XmlParticle(MinOccurs, MaxOccurs);
