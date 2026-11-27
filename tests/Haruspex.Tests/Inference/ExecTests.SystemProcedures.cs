using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

// SQL Server's own procedures the catalogue lists (SystemProcedures). Observed:
// Sp_getapplock_and_sp_releaseapplock_return_no_result_set_and_an_int,
// It_returns_no_result_set_and_its_return_code_is_0_for_a_2xx_status_and_the_status_otherwise (RestEndpointObservationTests).
public partial class ExecTests
{
    [Theory]
    [InlineData("EXEC @lock = sys.sp_getapplock @Resource = N'shop.nightly', @LockMode = N'Exclusive';")]
    [InlineData("EXEC @lock = sp_getapplock N'shop.nightly', N'Exclusive', N'Session', 0;")]
    [InlineData("EXEC @lock = dbo.sp_releaseapplock @Resource = N'shop.nightly';")]
    [InlineData("EXECUTE @lock = sys.Sp_GetAppLock @Resource = N'shop.nightly', @LockMode = N'Shared';")]
    public void A_lock_procedure_returns_no_result_set_and_sets_its_return_code(string call)
    {
        // The procedure's own results are what it returns besides the call, and the return code is never null.
        var doc = Infer(("p", $"""
            CREATE PROCEDURE dbo.p AS
            DECLARE @lock int;
            {call}
            SELECT t.id, @lock AS lock FROM dbo.t AS t;
            """));

        Assert.Single(Results(doc));
        Assert.False(Field(doc, "lock").Nullable);
        Assert.Empty(doc.Unknowns);
    }

    [Fact]
    public void A_lock_procedure_leaves_the_callers_temporary_tables_as_they_were()
    {
        var doc = Infer(("p", """
            CREATE PROCEDURE dbo.p AS
            SELECT t.id INTO #ids FROM dbo.t AS t;
            EXEC sys.sp_getapplock @Resource = N'shop.nightly', @LockMode = N'Exclusive';
            SELECT i.id FROM #ids AS i;
            """));

        Assert.False(Field(doc, "id").Nullable);
        Assert.Empty(doc.Unknowns);
    }

    [Fact]
    public void A_procedure_of_the_project_named_like_a_catalogued_system_procedure_is_the_system_one()
    {
        // SQL Server runs its own whatever the project defines (A_system_procedures_name_runs_the_system_procedure_whatever_the_project_defines).
        var doc = Infer(
            ("sp_getapplock", "CREATE PROCEDURE dbo.sp_getapplock @Resource nvarchar(255), @LockMode varchar(32) AS SELECT t.id FROM dbo.t AS t;"),
            ("p", "CREATE PROCEDURE dbo.p AS EXEC dbo.sp_getapplock N'shop.nightly', 'Exclusive'; SELECT 1 AS one;"));

        Assert.Equal(["one"], Fields(doc, Results(doc)[0].Type).Select(f => f.Name));
    }

    [Fact]
    public void A_rest_endpoint_call_returns_no_result_set_and_sets_its_return_code()
    {
        // 0 for a 2xx status, the status otherwise: an int, never null once the call returns. RestEndpointTests types @response.
        var doc = Infer(("p", """
            CREATE PROCEDURE dbo.p AS
            DECLARE @response nvarchar(max), @status int;
            EXEC @status = sys.sp_invoke_external_rest_endpoint @url = N'https://example.invalid/shop', @method = N'GET', @response = @response OUTPUT;
            SELECT t.id, @status AS status FROM dbo.t AS t;
            """));

        Assert.Single(Results(doc));
        Assert.False(Field(doc, "status").Nullable);
        Assert.Empty(doc.Unknowns);
    }
}
