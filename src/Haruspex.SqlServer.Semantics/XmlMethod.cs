namespace Haruspex.SqlServer.Semantics;

/// <summary>The methods of an <c>xml</c> value an expression may call (<c>x.value(…)</c>), apart from the built-in functions.</summary>
public enum XmlMethod
{
    /// <summary><c>value('path', 'type')</c>: the node the path finds, as the type named.</summary>
    Value,

    /// <summary><c>exist('path')</c>: whether the path finds a node.</summary>
    Exist,

    /// <summary><c>query('path')</c>: the XML the path finds.</summary>
    Query,

    /// <summary><c>nodes('path')</c>, a source in <c>FROM</c>: a row for each node the path finds.</summary>
    Nodes,
}
