namespace Haruspex.SqlServer.Model.XmlSchemas;

/// <summary>A name in XML: an element's, an attribute's or a type's, in its namespace.</summary>
/// <param name="Namespace">Its namespace URI; empty for none.</param>
/// <param name="Local">Its local name.</param>
public sealed record XmlName(string Namespace, string Local)
{
    /// <summary>The name as messages give it: <c>{urn:shop}order</c>, or <c>order</c> in no namespace.</summary>
    public override string ToString() => Namespace.Length == 0 ? Local : $"{{{Namespace}}}{Local}";
}
