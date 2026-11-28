namespace Haruspex.Tests.Inference;

public partial class FunctionTests
{
    [Fact]
    public void Rowcount_trancount_and_error_are_never_null_wherever_they_are_read()
    {
        // Rowcount_trancount_and_error_are_int_rowcount_big_a_bigint_and_none_is_ever_null: at the start of a body, after a
        // query, inside a CATCH and after an INSERT alike.
        var doc = Procedure("""
            DECLARE @w int;
            BEGIN TRY
                SET @w = 1 / 0;
            END TRY
            BEGIN CATCH
                INSERT INTO dbo.t (id, g, n, ti, s, c, d, f, m, da, dt, d2, status) VALUES (1, 1, 1, 1, 'a', 'a', 1, 1, 1, '2026-01-01', '2026-01-01', '2026-01-01', 'open');
                SELECT @@ROWCOUNT AS a, ROWCOUNT_BIG() AS b, @@TRANCOUNT AS c, @@ERROR AS d, @@rowcount AS e;
            END CATCH;
            """);
        Assert.Equal("int", Typed(doc, "a"));
        Assert.Equal("bigint", Typed(doc, "b"));
        Assert.Equal("int", Typed(doc, "c"));
        Assert.Equal("int", Typed(doc, "d"));
        Assert.Equal("int", Typed(doc, "e"));
    }

    [Fact]
    public void A_global_variable_not_in_the_catalogue_is_still_unknown()
    {
        var doc = Procedure("SELECT @@SPID AS a, @@VERSION AS b;");
        Unknown(doc, "a");
        Unknown(doc, "b");
    }
}
