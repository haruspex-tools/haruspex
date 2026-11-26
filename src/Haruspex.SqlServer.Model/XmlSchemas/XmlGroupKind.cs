namespace Haruspex.SqlServer.Model.XmlSchemas;

/// <summary>How a group's particles occur.</summary>
public enum XmlGroupKind
{
    /// <summary>Each in order.</summary>
    Sequence,

    /// <summary>One of them.</summary>
    Choice,

    /// <summary>Each, in any order.</summary>
    All,
}
