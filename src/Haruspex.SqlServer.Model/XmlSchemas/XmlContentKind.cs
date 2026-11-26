namespace Haruspex.SqlServer.Model.XmlSchemas;

/// <summary>What a complex type's content holds.</summary>
public enum XmlContentKind
{
    /// <summary>Nothing.</summary>
    Empty,

    /// <summary>Text of a simple type, and attributes: simple content.</summary>
    Simple,

    /// <summary>Elements only.</summary>
    Elements,

    /// <summary>Elements and text.</summary>
    Mixed,
}
