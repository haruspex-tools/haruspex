using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// A clause a function call may carry besides its name and its arguments, each a property of ScriptDom's
/// <see cref="FunctionCall"/> (<see cref="CallClauses"/>). A row of the function table says which it reads
/// (<see cref="FunctionTyping.Reads"/>), and a call carrying any other is not modelled: read as the call without it, it
/// would be a claim about another call (docs/decisions/0113-a-clause-no-rule-reads-makes-the-call-unknown.md).
/// </summary>
[Flags]
internal enum Clause
{
    /// <summary>No clause.</summary>
    None = 0,

    /// <summary><c>OVER (…)</c>: the call is a window function.</summary>
    Over = 1 << 0,

    /// <summary><c>DISTINCT</c> before an aggregate's argument.</summary>
    Distinct = 1 << 1,

    /// <summary><c>WITHIN GROUP (ORDER BY …)</c>.</summary>
    WithinGroup = 1 << 2,

    /// <summary><c>IGNORE NULLS</c> or <c>RESPECT NULLS</c>.</summary>
    NullTreatment = 1 << 3,

    /// <summary><c>LEADING</c>, <c>TRAILING</c> or <c>BOTH</c> in <c>TRIM</c>.</summary>
    TrimSide = 1 << 4,

    /// <summary>A JSON constructor's <c>key: value</c> pairs.</summary>
    KeyValuePairs = 1 << 5,

    /// <summary>A JSON aggregate's <c>ORDER BY</c>.</summary>
    JsonOrderBy = 1 << 6,

    /// <summary><c>NULL ON NULL</c> or <c>ABSENT ON NULL</c>.</summary>
    NullOnNull = 1 << 7,

    /// <summary><c>RETURNING</c> a type.</summary>
    Returning = 1 << 8,

    /// <summary><c>WITH ARRAY WRAPPER</c>.</summary>
    ArrayWrapper = 1 << 9,

    /// <summary><c>WITHIN GROUP (GRAPH PATH)</c>, in place of an order, over a <c>MATCH</c>'s shortest path.</summary>
    GraphPath = 1 << 10,
}
