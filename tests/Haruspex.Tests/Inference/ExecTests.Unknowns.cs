using Haruspex.Ir;

namespace Haruspex.Tests.Inference;

public partial class ExecTests
{
    [Theory]
    [InlineData("DECLARE @sql nvarchar(max) = N'SELECT 1 AS a'; EXEC (@sql);")]
    [InlineData("EXEC (N'SELECT 1 AS a');")]
    [InlineData("EXECUTE (N'SELECT ' + N'1 AS a');")]
    [InlineData("EXEC sp_executesql N'SELECT 1 AS a';")]
    [InlineData("EXEC sys.sp_executesql N'SELECT 1 AS a';")]
    [InlineData("DECLARE @rc int; EXEC @rc = dbo.sp_executesql N'SELECT 1 AS a';")]
    public void Dynamic_sql_makes_the_results_unknown(string call)
    {
        var why = ResultsUnknown(Infer(("p", $"CREATE PROCEDURE dbo.p AS SELECT t.id FROM dbo.t AS t; {call}")));

        Assert.Equal(IrUnknownReasons.DynamicSql, why.Reason);
        Assert.Contains("dynamic SQL", why.Detail, StringComparison.Ordinal);
        Assert.StartsWith("p.sql:1:", why.At, StringComparison.Ordinal);
    }

    [Fact]
    public void A_procedure_named_in_a_variable_is_unknown()
    {
        var why = ResultsUnknown(Infer(("p", "CREATE PROCEDURE dbo.p AS DECLARE @name sysname = N'dbo.q'; EXEC @name;")));

        Assert.Equal(IrUnknownReasons.ProcedureVariable, why.Reason);
    }

    [Theory]
    [InlineData("EXEC sp_who;")]
    [InlineData("EXEC sys.sp_who;")]
    [InlineData("EXEC dbo.sp_help N'dbo.t';")]
    [InlineData("EXEC master.dbo.xp_fixeddrives;")]
    [InlineData("EXEC xp_fixeddrives;")]
    public void A_system_procedure_is_unknown(string call)
    {
        var why = ResultsUnknown(Infer(("p", $"CREATE PROCEDURE dbo.p AS {call}")));

        Assert.Equal(IrUnknownReasons.SystemProcedure, why.Reason);
    }

    [Fact]
    public void A_procedure_of_the_project_named_like_a_system_procedure_is_unknown()
    {
        // Observed: A_system_procedures_name_runs_the_system_procedure_whatever_the_project_defines. Which system procedures
        // exist is not known here, so any sp_ name may be one.
        var why = ResultsUnknown(Infer(
            ("sp_mine", "CREATE PROCEDURE dbo.sp_mine AS SELECT t.id FROM dbo.t AS t;"),
            ("p", "CREATE PROCEDURE dbo.p AS EXEC dbo.sp_mine;")));

        Assert.Equal(IrUnknownReasons.SystemProcedure, why.Reason);
        Assert.Contains("dbo.sp_mine", why.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("EXEC dbo.nowhere;")]
    [InlineData("EXEC nowhere;")]
    [InlineData("EXEC other_db.dbo.q;")]
    [InlineData("EXEC remote_server.other_db.dbo.q;")]
    [InlineData("EXEC (N'SELECT 1') AT remote_server;")]
    public void A_procedure_not_in_the_project_is_unknown(string call)
    {
        var why = ResultsUnknown(Infer(
            ("q", "CREATE PROCEDURE dbo.q AS SELECT t.id FROM dbo.t AS t;"),
            ("p", $"CREATE PROCEDURE dbo.p AS {call}")));

        Assert.Equal(IrUnknownReasons.MissingProcedure, why.Reason);
    }

    [Fact]
    public void An_unqualified_name_two_schemas_have_is_ambiguous()
    {
        var why = ResultsUnknown(Infer(
            "dbo.p",
            ("q", "CREATE PROCEDURE dbo.q AS SELECT t.id FROM dbo.t AS t;"),
            ("q2", "CREATE SCHEMA other;\nGO\nCREATE PROCEDURE other.q AS SELECT t.name FROM dbo.t AS t;"),
            ("p", "CREATE PROCEDURE dbo.p AS EXEC q;")));

        Assert.Equal(IrUnknownReasons.AmbiguousProcedure, why.Reason);
    }

    [Fact]
    public void A_clr_procedure_is_unknown()
    {
        var why = ResultsUnknown(Infer(
            ("q", "CREATE PROCEDURE dbo.q AS EXTERNAL NAME Lib.[Lib.Procs].Q;"),
            ("p", "CREATE PROCEDURE dbo.p AS EXEC dbo.q;")));

        Assert.Equal(IrUnknownReasons.ExternalCode, why.Reason);
    }

    [Fact]
    public void A_callees_reason_reaches_its_caller_naming_the_call()
    {
        var doc = Infer(
            ("q", "CREATE PROCEDURE dbo.q AS DECLARE @sql nvarchar(max) = N'SELECT 1 AS a'; EXEC (@sql);"),
            ("p", "CREATE PROCEDURE dbo.p AS EXEC dbo.q;"));

        var why = ResultsUnknown(doc);
        Assert.Equal(IrUnknownReasons.DynamicSql, why.Reason);
        Assert.StartsWith("q.sql:1:", why.At, StringComparison.Ordinal);
        Assert.Contains("dbo.q", why.Detail, StringComparison.Ordinal);
        Assert.Contains("p.sql:1:", why.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_redefined_callee_is_unknown()
    {
        var why = ResultsUnknown(Infer(
            ("q", "CREATE PROCEDURE dbo.q AS SELECT t.id FROM dbo.t AS t;\nGO\nALTER PROCEDURE dbo.q AS SELECT t.name FROM dbo.t AS t;"),
            ("p", "CREATE PROCEDURE dbo.p AS EXEC dbo.q;")));

        Assert.Equal(IrUnknownReasons.Redefined, why.Reason);
    }
}
