namespace Haruspex.SqlServer.Model.XmlSchemas;

/// <summary>A type of an element or attribute: an <see cref="XmlSimpleTypeDeclaration"/> or an <see cref="XmlComplexTypeDeclaration"/>.</summary>
/// <remarks>A closed hierarchy (docs/decisions/0031-the-model-is-closed-hierarchies-of-records.md).</remarks>
public abstract record XmlTypeDeclaration
{
    /// <summary>Creates a type; only the kinds in this assembly can.</summary>
    /// <param name="name">Its name; null for an anonymous type.</param>
    private protected XmlTypeDeclaration(XmlName? name)
    {
        Name = name;
    }

    /// <summary>Its name; null for an anonymous type.</summary>
    public XmlName? Name { get; }
}
