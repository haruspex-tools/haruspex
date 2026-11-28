using Haruspex.Ir;

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

    private const string Sequences = """
        CREATE SCHEMA sales;
        GO
        CREATE TYPE dbo.small FROM smallint NOT NULL;
        GO
        CREATE SEQUENCE dbo.counted AS int START WITH 1;
        CREATE SEQUENCE dbo.tiny AS tinyint;
        CREATE SEQUENCE dbo.exact AS decimal(10, 0);
        CREATE SEQUENCE dbo.short AS numeric(5);
        CREATE SEQUENCE dbo.plain START WITH 1;
        CREATE SEQUENCE dbo.aliased AS dbo.small;
        CREATE SEQUENCE dbo.shared AS int;
        CREATE SEQUENCE sales.shared AS tinyint;
        CREATE SEQUENCE sales.own AS smallint;
        GO
        """;

    private static IrDocument InSchema(string procedure, string body) =>
        InferProcedure.Infer(procedure, ("tables.sql", Tables), ("sequences.sql", Sequences), ("p.sql", $"CREATE PROCEDURE {procedure} AS\n{body}"));

    [Fact]
    public void Next_value_for_is_its_sequences_type_never_null()
    {
        // Next_value_for_is_its_sequences_type_never_null_and_an_error_once_a_sequence_without_cycle_is_spent.
        var doc = InSchema("dbo.p", """
            SELECT NEXT VALUE FOR dbo.counted AS a, NEXT VALUE FOR dbo.tiny AS b, NEXT VALUE FOR dbo.exact AS c, NEXT VALUE FOR dbo.short AS d,
                   NEXT VALUE FOR dbo.plain AS e, NEXT VALUE FOR dbo.aliased AS f, NEXT VALUE FOR counted AS g;
            """);
        Assert.Equal("int", Typed(doc, "a"));
        Assert.Equal("tinyint", Typed(doc, "b"));
        Assert.Equal("decimal(10,0)", Typed(doc, "c"));
        Assert.Equal("numeric(5,0)", Typed(doc, "d"));
        Assert.Equal("bigint", Typed(doc, "e"));
        Assert.Equal("smallint", Typed(doc, "f"));
        Assert.Equal("int", Typed(doc, "g"));
    }

    [Fact]
    public void An_unqualified_sequence_is_the_procedures_schemas_then_dbos()
    {
        // An_unqualified_sequence_in_a_procedure_is_looked_for_in_the_procedures_schema_then_dbo: whoever calls.
        var doc = InSchema("sales.p", "SELECT NEXT VALUE FOR shared AS a, NEXT VALUE FOR own AS b, NEXT VALUE FOR counted AS c, NEXT VALUE FOR dbo.shared AS d;");
        Assert.Equal("tinyint", Typed(doc, "a"));
        Assert.Equal("smallint", Typed(doc, "b"));
        Assert.Equal("int", Typed(doc, "c"));
        Assert.Equal("int", Typed(doc, "d"));
    }

    [Fact]
    public void A_sequence_the_project_lacks_or_the_caller_chooses_is_unknown()
    {
        // A sequence the project doesn't define, one in another database, and NEXT VALUE FOR with OVER.
        var doc = InSchema("dbo.p", """
            SELECT NEXT VALUE FOR dbo.missing AS a, NEXT VALUE FOR sales.counted AS b, NEXT VALUE FOR other.dbo.counted AS c,
                   NEXT VALUE FOR dbo.counted OVER (ORDER BY n) AS d, n AS e
            FROM dbo.t;
            """);
        Unknown(doc, "a");
        Unknown(doc, "b");
        Unknown(doc, "c");
        Unknown(doc, "d");
        Assert.Equal("int", Typed(doc, "e"));
    }
}
