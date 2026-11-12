namespace Haruspex.SqlServer.Semantics;

/// <summary>The built-in window functions, called with <c>OVER</c>, in the catalogue (<see cref="BuiltInFunctions"/>).</summary>
public static class WindowFunctions
{
    /// <summary><c>ROW_NUMBER</c>.</summary>
    public static readonly BuiltInFunction RowNumber = new("row_number", FunctionForm.Window);

    /// <summary><c>RANK</c>.</summary>
    public static readonly BuiltInFunction Rank = new("rank", FunctionForm.Window);

    /// <summary><c>DENSE_RANK</c>.</summary>
    public static readonly BuiltInFunction DenseRank = new("dense_rank", FunctionForm.Window);

    /// <summary><c>NTILE</c>.</summary>
    public static readonly BuiltInFunction NTile = new("ntile", FunctionForm.Window);

    /// <summary><c>COUNT</c>.</summary>
    public static readonly BuiltInFunction Count = new("count", FunctionForm.Window);

    /// <summary><c>COUNT_BIG</c>.</summary>
    public static readonly BuiltInFunction CountBig = new("count_big", FunctionForm.Window);

    /// <summary><c>SUM</c>.</summary>
    public static readonly BuiltInFunction Sum = new("sum", FunctionForm.Window);

    /// <summary><c>AVG</c>.</summary>
    public static readonly BuiltInFunction Avg = new("avg", FunctionForm.Window);

    /// <summary><c>STDEV</c>.</summary>
    public static readonly BuiltInFunction Stdev = new("stdev", FunctionForm.Window);

    /// <summary><c>STDEVP</c>.</summary>
    public static readonly BuiltInFunction StdevP = new("stdevp", FunctionForm.Window);

    /// <summary><c>VAR</c>.</summary>
    public static readonly BuiltInFunction Var = new("var", FunctionForm.Window);

    /// <summary><c>VARP</c>.</summary>
    public static readonly BuiltInFunction VarP = new("varp", FunctionForm.Window);

    /// <summary><c>MIN</c>.</summary>
    public static readonly BuiltInFunction Min = new("min", FunctionForm.Window);

    /// <summary><c>MAX</c>.</summary>
    public static readonly BuiltInFunction Max = new("max", FunctionForm.Window);

    /// <summary><c>FIRST_VALUE</c>.</summary>
    public static readonly BuiltInFunction FirstValue = new("first_value", FunctionForm.Window);

    /// <summary><c>LAST_VALUE</c>.</summary>
    public static readonly BuiltInFunction LastValue = new("last_value", FunctionForm.Window);

    /// <summary><c>LAG</c>.</summary>
    public static readonly BuiltInFunction Lag = new("lag", FunctionForm.Window);

    /// <summary><c>LEAD</c>.</summary>
    public static readonly BuiltInFunction Lead = new("lead", FunctionForm.Window);

    /// <summary><c>PERCENTILE_CONT</c>.</summary>
    public static readonly BuiltInFunction PercentileCont = new("percentile_cont", FunctionForm.Window);

    /// <summary><c>PERCENTILE_DISC</c>.</summary>
    public static readonly BuiltInFunction PercentileDisc = new("percentile_disc", FunctionForm.Window);

    /// <summary>Every one, in the order above.</summary>
    public static IReadOnlyList<BuiltInFunction> All { get; } = [RowNumber, Rank, DenseRank, NTile, Count, CountBig, Sum, Avg, Stdev, StdevP, Var, VarP, Min, Max, FirstValue, LastValue, Lag, Lead, PercentileCont, PercentileDisc];
}
