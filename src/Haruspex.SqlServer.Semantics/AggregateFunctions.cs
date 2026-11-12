namespace Haruspex.SqlServer.Semantics;

/// <summary>The built-in aggregates, one value per group, in the catalogue (<see cref="BuiltInFunctions"/>).</summary>
public static class AggregateFunctions
{
    /// <summary><c>COUNT</c>.</summary>
    public static readonly BuiltInFunction Count = new("count", FunctionForm.Aggregate);

    /// <summary><c>COUNT_BIG</c>.</summary>
    public static readonly BuiltInFunction CountBig = new("count_big", FunctionForm.Aggregate);

    /// <summary><c>SUM</c>.</summary>
    public static readonly BuiltInFunction Sum = new("sum", FunctionForm.Aggregate);

    /// <summary><c>AVG</c>.</summary>
    public static readonly BuiltInFunction Avg = new("avg", FunctionForm.Aggregate);

    /// <summary><c>MIN</c>.</summary>
    public static readonly BuiltInFunction Min = new("min", FunctionForm.Aggregate);

    /// <summary><c>MAX</c>.</summary>
    public static readonly BuiltInFunction Max = new("max", FunctionForm.Aggregate);

    /// <summary><c>STDEV</c>.</summary>
    public static readonly BuiltInFunction Stdev = new("stdev", FunctionForm.Aggregate);

    /// <summary><c>STDEVP</c>.</summary>
    public static readonly BuiltInFunction StdevP = new("stdevp", FunctionForm.Aggregate);

    /// <summary><c>VAR</c>.</summary>
    public static readonly BuiltInFunction Var = new("var", FunctionForm.Aggregate);

    /// <summary><c>VARP</c>.</summary>
    public static readonly BuiltInFunction VarP = new("varp", FunctionForm.Aggregate);

    /// <summary><c>STRING_AGG</c>.</summary>
    public static readonly BuiltInFunction StringAgg = new("string_agg", FunctionForm.Aggregate);

    /// <summary><c>CHECKSUM_AGG</c>.</summary>
    public static readonly BuiltInFunction ChecksumAgg = new("checksum_agg", FunctionForm.Aggregate);

    /// <summary><c>GROUPING</c>.</summary>
    public static readonly BuiltInFunction Grouping = new("grouping", FunctionForm.Aggregate);

    /// <summary><c>GROUPING_ID</c>.</summary>
    public static readonly BuiltInFunction GroupingId = new("grouping_id", FunctionForm.Aggregate);

    /// <summary><c>APPROX_COUNT_DISTINCT</c>.</summary>
    public static readonly BuiltInFunction ApproxCountDistinct = new("approx_count_distinct", FunctionForm.Aggregate);

    /// <summary><c>JSON_ARRAYAGG</c>.</summary>
    public static readonly BuiltInFunction JsonArrayAgg = new("json_arrayagg", FunctionForm.Aggregate);

    /// <summary><c>JSON_OBJECTAGG</c>.</summary>
    public static readonly BuiltInFunction JsonObjectAgg = new("json_objectagg", FunctionForm.Aggregate);

    /// <summary>Every one, in the order above.</summary>
    public static IReadOnlyList<BuiltInFunction> All { get; } = [Count, CountBig, Sum, Avg, Min, Max, Stdev, StdevP, Var, VarP, StringAgg, ChecksumAgg, Grouping, GroupingId, ApproxCountDistinct, JsonArrayAgg, JsonObjectAgg];
}
