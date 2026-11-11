namespace Haruspex.SqlServer.Model;

/// <summary>What a <see cref="NamedObject"/> is.</summary>
public enum NamedObjectKind
{
    /// <summary>A view.</summary>
    View,

    /// <summary>A synonym.</summary>
    Synonym,

    /// <summary>A function of any kind: scalar, table-valued or CLR.</summary>
    Function,
}
