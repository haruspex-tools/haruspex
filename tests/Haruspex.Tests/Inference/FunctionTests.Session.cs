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

    [Fact]
    public void Ident_current_is_numeric_38_0_and_always_out_of_range()
    {
        // Ident_current_is_numeric_38_0_and_null_for_a_table_without_an_identity_a_missing_one_or_one_the_caller_cannot_see:
        // NULL for a name the project defines too, since the caller may not be allowed to see it.
        var doc = Procedure("SELECT IDENT_CURRENT('dbo.t') AS a, IDENT_CURRENT(ns) AS b, IDENT_CURRENT(vm) AS c, IDENT_CURRENT(n) AS d, IDENT_CURRENT() AS e FROM dbo.t;");
        Assert.Equal("numeric(38,0) out-of-range", Typed(doc, "a"));
        Assert.Equal("numeric(38,0) nullable-column out-of-range", Typed(doc, "b"));
        Assert.Equal("numeric(38,0) nullable-column out-of-range", Typed(doc, "c"));
        Unknown(doc, "d");
        Unknown(doc, "e");
    }

    [Fact]
    public void User_name_of_an_id_is_nvarchar_128_out_of_range_and_overflows_from_a_bigint()
    {
        // User_name_of_an_id_is_nvarchar_128_and_null_for_an_id_that_names_no_user. USER_NAME() without an id is not modelled.
        var doc = Procedure("SELECT USER_NAME(n) AS a, USER_NAME(ti) AS b, USER_NAME(bi) AS c, USER_NAME(1) AS d, USER_NAME(s) AS e, USER_NAME() AS f FROM dbo.t;");
        Assert.Equal("nvarchar(128) out-of-range", Typed(doc, "a"));
        Assert.Equal("nvarchar(128) out-of-range", Typed(doc, "b"));
        Assert.Equal("nvarchar(128) nullable-column out-of-range arithmetic-overflow", Typed(doc, "c"));
        Assert.Equal("nvarchar(128) out-of-range", Typed(doc, "d"));
        Unknown(doc, "e");
        Unknown(doc, "f");
    }
}
