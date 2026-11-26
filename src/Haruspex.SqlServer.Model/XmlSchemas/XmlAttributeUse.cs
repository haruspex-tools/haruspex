namespace Haruspex.SqlServer.Model.XmlSchemas;

/// <summary>Whether an element always has an attribute.</summary>
public enum XmlAttributeUse
{
    /// <summary>It may be missing.</summary>
    Optional,

    /// <summary><c>use="required"</c>: always there.</summary>
    Required,

    /// <summary>It has a default or fixed value, which SQL Server writes into the instance where it is missing, so it is always there.</summary>
    Defaulted,

    /// <summary><c>use="prohibited"</c>: never there.</summary>
    Prohibited,
}
