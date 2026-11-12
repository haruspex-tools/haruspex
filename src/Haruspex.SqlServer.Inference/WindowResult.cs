namespace Haruspex.SqlServer.Inference;

/// <summary>What a window function gives (<see cref="WindowRule"/>).</summary>
internal enum WindowResult
{
    /// <summary>A type of its own, whatever its argument: <c>ROW_NUMBER</c> is bigint.</summary>
    Fixed,

    /// <summary>The type <c>SUM</c> gives its argument.</summary>
    Sum,

    /// <summary>The type <c>AVG</c> or a statistic gives its argument.</summary>
    Statistic,

    /// <summary>One of its argument's values, with its type and facets: <c>MIN</c>, <c>FIRST_VALUE</c>.</summary>
    Argument,

    /// <summary>A value of another row, or a default: <c>LAG</c> and <c>LEAD</c>.</summary>
    Offset,

    /// <summary>A float between two of its argument's values: <c>PERCENTILE_CONT</c>.</summary>
    PercentileBetweenValues,

    /// <summary>One of its argument's values: <c>PERCENTILE_DISC</c>.</summary>
    PercentileOfValues,
}
