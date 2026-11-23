namespace Haruspex.DifferentialTests;

// A T-SQL scalar function's value, converted to the type it declares it returns: the rule FunctionSummaries reads a call by.
public partial class FunctionObservationTests
{
    /// <summary>Scalar functions each returning a value its declared type cuts, rounds, pads or can't hold.</summary>
    private static readonly string[] Returning =
    [
        "CREATE FUNCTION dbo.obs_short() RETURNS varchar(2) AS BEGIN RETURN 'abcdef'; END;",
        "CREATE FUNCTION dbo.obs_rounded() RETURNS decimal(5, 2) AS BEGIN RETURN 1.005; END;",
        "CREATE FUNCTION dbo.obs_padded() RETURNS nchar(4) AS BEGIN RETURN N'a'; END;",
        "CREATE FUNCTION dbo.obs_cut(@s nvarchar(10)) RETURNS nvarchar(3) AS BEGIN RETURN @s; END;",
        "CREATE FUNCTION dbo.obs_overflow() RETURNS decimal(3, 1) AS BEGIN RETURN 12345.6; END;",
    ];

    [Fact]
    public async Task A_scalar_functions_value_is_converted_to_its_declared_return_type() =>
        // Cut short without an error, rounded, and padded, as a variable of the type is.
        Assert.Equal(
            "s,n,d,c,v: ab,2,1.01,[a   ],abc",
            (await Run(Returning, "SELECT dbo.obs_short() AS s, DATALENGTH(dbo.obs_short()) AS n, dbo.obs_rounded() AS d, '[' + dbo.obs_padded() + ']' AS c, dbo.obs_cut(N'abcdefg') AS v;")).ToString());

    [Fact]
    public async Task A_scalar_function_call_is_described_as_its_declared_return_type() =>
        Assert.Equal(
            "system_type_name: varchar(2);decimal(5,2);nchar(4);nvarchar(3)",
            (await Run(Returning, "SELECT system_type_name FROM sys.dm_exec_describe_first_result_set(N'SELECT dbo.obs_short(), dbo.obs_rounded(), dbo.obs_padded(), dbo.obs_cut(N''x'')', NULL, 0) ORDER BY column_ordinal;")).ToString());

    [Theory]
    [InlineData("SELECT dbo.obs_overflow() AS x;", " ! 8115")]
    [InlineData("SET ANSI_WARNINGS OFF; SET ARITHABORT OFF; SELECT dbo.obs_overflow() AS x;", "x: null")]
    // A value its declared type can't hold is an error, or NULL with ANSI_WARNINGS and ARITHABORT off, never a value outside
    // the type: the null the function-result reason already allows.
    public async Task A_scalar_functions_value_its_return_type_cannot_hold_is_an_error_or_null(string call, string outcome) =>
        Assert.Equal(outcome, (await Run(Returning, call)).ToString());
}
