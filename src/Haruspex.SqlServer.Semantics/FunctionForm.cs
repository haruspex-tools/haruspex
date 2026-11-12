namespace Haruspex.SqlServer.Semantics;

/// <summary>How a built-in function is called, which decides what it gives.</summary>
public enum FunctionForm
{
    /// <summary>One value per row from its arguments: <c>DATEDIFF</c>, <c>CAST</c>.</summary>
    Scalar,

    /// <summary>One value per group, or per query without <c>GROUP BY</c>: <c>SUM(x)</c>.</summary>
    Aggregate,

    /// <summary>One value per row over a window, called with <c>OVER</c>: <c>SUM(x) OVER (…)</c>.</summary>
    Window,

    /// <summary>Rows, read in <c>FROM</c>: <c>GENERATE_SERIES</c>, <c>OPENJSON</c>.</summary>
    Rowset,
}
