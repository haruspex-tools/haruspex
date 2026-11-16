namespace Haruspex.Tests.Inference;

/// <summary>
/// Whether a call may end with a return code other than 0 (<c>returnsCode</c>): a <c>RETURN</c> whose value isn't the constant 0
/// or NULL, anywhere in the body, as <c>MssqlDriverObservationTests</c> observes; a body that can't be read may.
/// </summary>
public partial class StatementFlowTests
{
    private static bool ReturnsCode(string body) =>
        Assert.Single(InferProcedure.Infer("dbo.p", ("p.sql", $"CREATE PROCEDURE dbo.p @n int AS {body}")).Objects).ReturnsCode;

    [Theory]
    [InlineData("SELECT 1 AS one;")]
    [InlineData("SELECT 1 AS one; RETURN;")]
    [InlineData("SELECT 1 AS one; RETURN 0;")]
    [InlineData("SELECT 1 AS one; RETURN (0);")]
    [InlineData("SELECT 1 AS one; RETURN NULL;")]
    public void A_body_that_returns_no_value_but_0_or_null_ends_with_the_code_0(string body) => Assert.False(ReturnsCode(body));

    [Theory]
    [InlineData("SELECT 1 AS one; RETURN 7;")]
    [InlineData("IF @n = 0 RETURN -1; SELECT 1 AS one;")]
    [InlineData("SELECT 1 AS one; RETURN @n;")]
    [InlineData("BEGIN TRY SELECT 1 AS one; END TRY BEGIN CATCH RETURN 2; END CATCH")]
    public void A_return_of_another_value_anywhere_may_end_with_a_code(string body) => Assert.True(ReturnsCode(body));

    [Fact]
    public void A_clr_procedure_may_end_with_a_code_since_its_body_is_not_read() =>
        Assert.True(Assert.Single(InferProcedure.Infer("dbo.p", ("p.sql", "CREATE PROCEDURE dbo.p AS EXTERNAL NAME a.b.c;")).Objects).ReturnsCode);

    [Fact]
    public void A_query_file_never_returns_a_code() =>
        Assert.False(InferQuery.Infer(InferQuery.Shop, "SELECT 1 AS one; RETURN;").Query().ReturnsCode);
}
