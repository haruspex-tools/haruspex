namespace Haruspex.SqlServer.Model.XmlSchemas;

/// <summary>
/// A part of a complex type's content model, occurring <see cref="MinOccurs"/> to <see cref="MaxOccurs"/> times: an element, a group of
/// particles, or a wildcard.
/// </summary>
/// <remarks>A closed hierarchy (docs/decisions/0031-the-model-is-closed-hierarchies-of-records.md).</remarks>
public abstract record XmlParticle
{
    /// <summary>Creates a particle; only the kinds in this assembly can.</summary>
    /// <param name="minOccurs">Its least number of occurrences.</param>
    /// <param name="maxOccurs">Its most; null for unbounded.</param>
    private protected XmlParticle(int minOccurs, int? maxOccurs)
    {
        MinOccurs = minOccurs;
        MaxOccurs = maxOccurs;
    }

    /// <summary>Its least number of occurrences.</summary>
    public int MinOccurs { get; }

    /// <summary>Its most; null for unbounded.</summary>
    public int? MaxOccurs { get; }
}
