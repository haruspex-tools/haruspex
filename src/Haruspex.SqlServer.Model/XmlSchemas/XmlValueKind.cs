namespace Haruspex.SqlServer.Model.XmlSchemas;

/// <summary>What kind of value a simple type holds, as far as its facets are read.</summary>
public enum XmlValueKind
{
    /// <summary>Any other: a list, a union, a float or double, a boolean, a date or time, binary, a name of another kind.</summary>
    Other,

    /// <summary>A string, its whitespace kept (<c>xs:string</c> and the types restricting it without a <c>whiteSpace</c> facet).</summary>
    Text,

    /// <summary>A string whose whitespace is replaced or collapsed (<c>xs:normalizedString</c>, <c>xs:token</c> and the types derived from them).</summary>
    NormalisedText,

    /// <summary>A whole number: <c>xs:integer</c> and the types derived from it.</summary>
    WholeNumber,

    /// <summary>A decimal: <c>xs:decimal</c>.</summary>
    DecimalNumber,
}
