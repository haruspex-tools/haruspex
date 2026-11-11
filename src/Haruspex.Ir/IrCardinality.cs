namespace Haruspex.Ir;

/// <summary>How many rows a query returns: what a generated client needs to return one row or a list.</summary>
public enum IrCardinality
{
    /// <summary>Any number, none included: nothing is proved.</summary>
    Many,

    /// <summary>None or one: <c>TOP (1)</c>, <c>FETCH NEXT 1</c>, or a filter that fixes every column of a key.</summary>
    AtMostOne,

    /// <summary>Exactly one: an aggregate query without <c>GROUP BY</c> or <c>HAVING</c>, or a query without <c>FROM</c> or a filter.</summary>
    One,
}
