using Haruspex.SqlServer.Semantics;
using Haruspex.Tests.Snapshotting;

namespace Haruspex.Tests.Semantics;

/// <summary>
/// The catalogue of built-in functions (<see cref="BuiltInFunctions"/>): each entry's code is its own and stays as it was, and
/// a name finds its entry in any case.
/// </summary>
public class BuiltInFunctionsTests
{
    [Fact]
    public void Every_code_is_held_as_a_snapshot_so_a_change_to_one_shows() =>
        Snapshot.Match(string.Join("\n", BuiltInFunctions.All.Select(f => f.Code)) + "\n", "BuiltInFunctionsTests.Codes", "txt");

    [Fact]
    public void No_two_functions_share_a_code_or_a_name_in_one_form()
    {
        Assert.Equal(BuiltInFunctions.All.Count, BuiltInFunctions.All.Select(f => f.Code).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(BuiltInFunctions.All.Count, BuiltInFunctions.All.Select(f => (f.Name, f.Form)).Distinct().Count());
    }

    [Fact]
    public void A_name_finds_its_function_in_its_form_in_any_case()
    {
        Assert.Same(ScalarFunctions.DateDiff, BuiltInFunctions.Named("DateDiff", FunctionForm.Scalar));
        Assert.Same(WindowFunctions.Count, BuiltInFunctions.Named("COUNT", FunctionForm.Window));
        Assert.Same(AggregateFunctions.Count, BuiltInFunctions.Named("count", FunctionForm.Aggregate));
        Assert.Null(BuiltInFunctions.Named("row_number", FunctionForm.Scalar));
        Assert.Null(BuiltInFunctions.Named("checksum", FunctionForm.Scalar));
    }
}
