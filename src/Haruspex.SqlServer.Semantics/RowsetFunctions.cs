namespace Haruspex.SqlServer.Semantics;

/// <summary>The built-in functions that give rows, read in <c>FROM</c>, in the catalogue (<see cref="BuiltInFunctions"/>).</summary>
public static class RowsetFunctions
{
    /// <summary><c>GENERATE_SERIES</c>.</summary>
    public static readonly BuiltInFunction GenerateSeries = new("generate_series", FunctionForm.Rowset);

    /// <summary><c>OPENJSON</c>.</summary>
    public static readonly BuiltInFunction OpenJson = new("openjson", FunctionForm.Rowset);

    /// <summary><c>STRING_SPLIT</c>.</summary>
    public static readonly BuiltInFunction StringSplit = new("string_split", FunctionForm.Rowset);

    /// <summary>Every one, in the order above.</summary>
    public static IReadOnlyList<BuiltInFunction> All { get; } = [GenerateSeries, OpenJson, StringSplit];
}
