using Haruspex.Diagnostics;
using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

/// <summary>
/// <c>EXEC … WITH RESULT SETS</c>: the declaration types the call, since the server enforces it. Each rule is observed in the
/// differential tests' <c>ExecObservationTests.ResultSets.cs</c>.
/// </summary>
public partial class ExecTests
{
    /// <summary>Infers the files, with what inference reported.</summary>
    private static Outcome<IrDocument> Run(params (string Name, string Sql)[] procedures) =>
        InferProcedure.Inferred("*.*", [("tables.sql", Tables), .. procedures.Select(p => ($"{p.Name}.sql", p.Sql))]);

    private static string[] Codes(Outcome<IrDocument> run) => [.. run.Diagnostics.Select(d => d.Code.ToString())];

    private static IrScalar Scalar(IrField field) => Assert.IsType<IrScalar>(field.Type);

    [Fact]
    public void A_declared_result_set_names_and_types_the_callees_rows()
    {
        // Observed: With_result_sets_renames_and_converts_the_callees_columns_or_refuses_them.
        var doc = Infer(
            ("q", "CREATE PROCEDURE dbo.q AS SELECT t.id, t.name FROM dbo.t AS t;"),
            ("p", "CREATE PROCEDURE dbo.p AS EXEC dbo.q WITH RESULT SETS ((orderId bigint NOT NULL, label nvarchar(5) NULL)); SELECT t.code FROM dbo.t AS t;"));

        var results = Results(doc);
        Assert.Equal(2, results.Count);
        Assert.Equal(["orderId", "label"], Names(doc, results[0].Type));
        Assert.Equal(["code"], Names(doc, results[1].Type));
        Assert.All(results, r => Assert.False(r.MayBeAbsent));
        var id = Field(doc, "orderId");
        Assert.Equal("bigint", Scalar(id).Sql);
        Assert.False(id.Nullable);
        var label = Field(doc, "label");
        Assert.Equal("nvarchar(5)", Scalar(label).Sql);
        Assert.Equal(5, Scalar(label).Facets?.Length?.Value);
        Assert.True(label.Nullable);
        Assert.DoesNotContain(doc.Unknowns, u => u.Object == "dbo.p");
    }

    [Fact]
    public void A_declared_column_claims_its_type_s_values_and_none_of_the_callee_s()
    {
        // 1000 into varchar(3) arrives as *, and a string is cut short: the callee's values, their set and their digits, are not
        // what arrives. Observed: A_declared_column_holds_each_value_converted_to_its_type.
        var doc = Infer(
            ("q", "CREATE PROCEDURE dbo.q AS SELECT 1000 AS n, CASE WHEN t.id > 1 THEN 'long' ELSE 'longer' END AS k FROM dbo.t AS t;"),
            ("p", "CREATE PROCEDURE dbo.p AS EXEC dbo.q WITH RESULT SETS ((n varchar(3) NOT NULL, k varchar(3) NOT NULL));"));

        foreach (var name in new[] { "n", "k" })
        {
            var facets = Scalar(Field(doc, name)).Facets;
            Assert.Equal("varchar(3)", Scalar(Field(doc, name)).Sql);
            Assert.Equal(3, facets?.Length?.Value);
            Assert.Null(facets?.Values);
            Assert.Null(facets?.Min);
            Assert.Null(facets?.MinLength);
        }
    }

    [Theory]
    [InlineData("n int NULL", true)]
    [InlineData("n int", true)]
    [InlineData("n dbo.never_null", true)]
    [InlineData("n int NOT NULL", false)]
    [InlineData("n dbo.never_null NOT NULL", false)]
    public void Only_not_null_written_in_the_declaration_makes_a_column_non_null(string column, bool nullable)
    {
        // Neither NULL nor NOT NULL follows the caller's ANSI_NULL_DFLT, and an alias type's NOT NULL adds nothing. Observed:
        // A_column_declared_neither_null_nor_not_null_follows_the_sessions_null_default. The callee's column is never null, which
        // proves nothing of the declared one.
        var doc = Infer(
            ("types", "CREATE TYPE dbo.never_null FROM int NOT NULL;"),
            ("q", "CREATE PROCEDURE dbo.q AS SELECT t.id FROM dbo.t AS t;"),
            ("p", $"CREATE PROCEDURE dbo.p AS EXEC dbo.q WITH RESULT SETS (({column}));"));

        var n = Field(doc, "n");
        Assert.Equal(nullable, n.Nullable);
        if (nullable)
            Assert.Equal(IrNullRule.NullableColumn, Assert.Single(n.NullableBecause).Rule);
    }

    [Fact]
    public void A_declared_set_arrives_whatever_the_callee_s_paths_since_any_other_count_fails_the_call()
    {
        // The callee may return its set or none, but a call that succeeds returned exactly the one declared. Observed:
        // With_result_sets_fixes_how_many_result_sets_arrive_and_any_other_number_fails_the_call.
        var doc = Infer(
            ("q", "CREATE PROCEDURE dbo.q @b bit AS IF @b = 1 RETURN; SELECT t.id FROM dbo.t AS t;"),
            ("p", "CREATE PROCEDURE dbo.p @b bit AS EXEC dbo.q @b WITH RESULT SETS ((id int NOT NULL)); SELECT t.name FROM dbo.t AS t;"));

        var results = Results(doc);
        Assert.Equal(2, results.Count);
        Assert.Equal(["id"], Names(doc, results[0].Type));
        Assert.Equal(["name"], Names(doc, results[1].Type));
        Assert.All(results, r => Assert.False(r.MayBeAbsent));
    }

    [Fact]
    public void A_declared_count_the_callee_never_returns_fails_every_call_and_is_warned_of()
    {
        var run = Run(
            ("q", "CREATE PROCEDURE dbo.q AS SELECT t.id FROM dbo.t AS t; SELECT t.name FROM dbo.t AS t;"),
            ("p", "CREATE PROCEDURE dbo.p @b bit AS SELECT t.code FROM dbo.t AS t; IF @b = 1 EXEC dbo.q WITH RESULT SETS ((id int)); SELECT t.id AS last FROM dbo.t AS t;"));

        Assert.Equal(["HAR2105"], Codes(run));
        Assert.Contains("declares 1 result set, but dbo.q returns 2", run.Diagnostics[0].Message, StringComparison.Ordinal);
        // The path through the call fails, so only the path around it returns the last set.
        var results = Results(run.Expected());
        Assert.Equal(["code"], Names(run.Expected(), results[0].Type));
        Assert.Equal(["last"], Names(run.Expected(), results[1].Type));
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void A_declared_set_of_another_width_than_the_callee_s_fails_every_call_and_is_warned_of()
    {
        // Observed: A_set_of_another_width_or_of_a_type_with_no_conversion_fails_the_call_even_with_no_rows.
        var run = Run(
            ("q", "CREATE PROCEDURE dbo.q AS SELECT t.id, t.name FROM dbo.t AS t;"),
            ("p", "CREATE PROCEDURE dbo.p AS EXEC dbo.q WITH RESULT SETS ((id int)); SELECT t.code FROM dbo.t AS t;"));

        Assert.Equal(["HAR2105"], Codes(run));
        Assert.Contains("1 column", run.Diagnostics[0].Message, StringComparison.Ordinal);
        Assert.Empty(Results(run.Expected()));
    }

    [Fact]
    public void None_over_a_callee_that_always_returns_a_set_fails_every_call()
    {
        var run = Run(
            ("q", "CREATE PROCEDURE dbo.q AS SELECT t.id FROM dbo.t AS t;"),
            ("p", "CREATE PROCEDURE dbo.p @b bit AS IF @b = 1 EXEC dbo.q WITH RESULT SETS NONE; SELECT t.code FROM dbo.t AS t;"));

        Assert.Equal(["HAR2105"], Codes(run));
        var result = Assert.Single(Results(run.Expected()));
        Assert.Equal(["code"], Names(run.Expected(), result.Type));
    }

    [Fact]
    public void None_over_a_callee_that_may_return_a_set_lets_none_through()
    {
        var run = Run(
            ("q", "CREATE PROCEDURE dbo.q @b bit AS IF @b = 1 SELECT t.id FROM dbo.t AS t;"),
            ("p", "CREATE PROCEDURE dbo.p @b bit AS EXEC dbo.q @b WITH RESULT SETS NONE; SELECT t.code FROM dbo.t AS t;"));

        Assert.Empty(run.Diagnostics);
        var result = Assert.Single(Results(run.Expected()));
        Assert.Equal(["code"], Names(run.Expected(), result.Type));
        Assert.False(result.MayBeAbsent);
    }

    [Theory]
    [InlineData("DECLARE @table sysname = N'order_lines'; DECLARE @sql nvarchar(200) = N'SELECT id, name FROM shop.' + @table; EXEC sp_executesql @sql")]
    [InlineData("DECLARE @sql nvarchar(200) = N'SELECT id, name FROM shop.order_lines'; EXEC (@sql)")]
    [InlineData("EXEC dbo.nowhere")]
    public void The_declaration_types_a_call_whose_callee_is_not_known(string call)
    {
        // Observed: With_result_sets_types_dynamic_sql_as_it_types_a_procedure. The server enforces the declaration whatever the
        // text runs, so the call is typed by it.
        var doc = Infer(("p", $"CREATE PROCEDURE dbo.p AS {call} WITH RESULT SETS ((id int NOT NULL, name nvarchar(50) NULL));"));

        var result = Assert.Single(Results(doc));
        Assert.False(result.MayBeAbsent);
        Assert.Equal(["id", "name"], Names(doc, result.Type));
        var id = Field(doc, "id");
        Assert.False(id.Nullable);
        Assert.Equal("-2147483648", Scalar(id).Facets?.Min?.Value);
        Assert.Equal("2147483647", Scalar(id).Facets?.Max?.Value);
        var name = Field(doc, "name");
        Assert.True(name.Nullable);
        Assert.Equal(50, Scalar(name).Facets?.Length?.Value);
        Assert.DoesNotContain(doc.Unknowns, u => u.Object == "dbo.p");
    }

    [Fact]
    public void A_call_whose_callee_is_not_known_still_forgets_the_temporary_tables()
    {
        var doc = Infer(("p", "CREATE PROCEDURE dbo.p AS CREATE TABLE #t (v int NOT NULL); EXEC (N'SELECT 1 AS a') WITH RESULT SETS ((a int)); SELECT x.v FROM #t AS x;"));

        Assert.Equal(2, Results(doc).Count);
        Assert.IsType<IrUnknownType>(Field(doc, "v", 1).Type);
    }

    [Fact]
    public void None_types_a_call_whose_callee_is_not_known_as_returning_nothing()
    {
        var doc = Infer(("p", "CREATE PROCEDURE dbo.p AS EXEC (N'UPDATE dbo.t SET code = 1') WITH RESULT SETS NONE; SELECT t.id FROM dbo.t AS t;"));

        Assert.Equal(["id"], Names(doc, Assert.Single(Results(doc)).Type));
    }

    [Fact]
    public void An_error_inside_the_call_reaches_catch_after_any_of_the_declared_sets()
    {
        // Observed: An_error_with_result_sets_raises_is_caught_by_the_callers_catch_after_the_sets_already_sent.
        var doc = Infer(
            ("q", "CREATE PROCEDURE dbo.q AS SELECT t.id FROM dbo.t AS t;"),
            ("p", "CREATE PROCEDURE dbo.p AS BEGIN TRY EXEC dbo.q WITH RESULT SETS ((a int NOT NULL)); END TRY BEGIN CATCH SELECT t.id AS c FROM dbo.t AS t; END CATCH"));

        var results = Results(doc);
        Assert.Equal(2, results.Count);
        Assert.Equal(["a", "c"], Assert.IsType<IrUnion>(results[0].Type).Variants.SelectMany(v => Names(doc, v)).Order(StringComparer.Ordinal));
        Assert.False(results[0].MayBeAbsent);
        Assert.Equal(["c"], Names(doc, results[1].Type));
        Assert.True(results[1].MayBeAbsent);
    }

    [Theory]
    [InlineData("dbo.q2")]
    [InlineData("dbo.nowhere")]
    public void An_error_after_the_first_of_two_declared_sets_reaches_catch_with_that_set_sent(string callee)
    {
        // Observed: An_error_with_result_sets_raises_is_caught_by_the_callers_catch_after_the_sets_already_sent (11536 after one
        // set). So CATCH's set may arrive first, second or third.
        var doc = Infer(
            ("q2", "CREATE PROCEDURE dbo.q2 AS SELECT t.id FROM dbo.t AS t; SELECT t.code FROM dbo.t AS t;"),
            ("p", $"CREATE PROCEDURE dbo.p AS BEGIN TRY EXEC {callee} WITH RESULT SETS ((a int NOT NULL), (b int NOT NULL)); END TRY BEGIN CATCH SELECT t.id AS c FROM dbo.t AS t; END CATCH"));

        var results = Results(doc);
        Assert.Equal(3, results.Count);
        string[] Variants(IrResult result) => result.Type is IrUnion union ? [.. union.Variants.SelectMany(v => Names(doc, v)).Order(StringComparer.Ordinal)] : Names(doc, result.Type);
        Assert.Equal(["a", "c"], Variants(results[0]));
        Assert.Equal(["b", "c"], Variants(results[1]));
        Assert.Equal(["c"], Variants(results[2]));
    }

    [Fact]
    public void As_object_and_as_type_take_a_table_s_and_a_table_type_s_columns_and_their_not_null()
    {
        // Observed: As_object_and_as_type_take_the_columns_of_a_table_or_a_table_type_and_as_for_xml_one_ntext_column. A CHECK
        // is not enforced on what arrives, so it gives no facet.
        var doc = Infer(
            ("shapes", "CREATE TABLE dbo.shape (a int NOT NULL CHECK (a > 0), b nvarchar(5) NULL);\nGO\nCREATE TYPE dbo.pair AS TABLE (c bigint NOT NULL, d varchar(4));"),
            ("q", "CREATE PROCEDURE dbo.q AS SELECT t.id, t.name FROM dbo.t AS t;"),
            ("p", "CREATE PROCEDURE dbo.p AS EXEC dbo.q WITH RESULT SETS (AS OBJECT dbo.shape); EXEC dbo.q WITH RESULT SETS (AS TYPE dbo.pair);"));

        Assert.Equal(["a", "b"], Names(doc, Results(doc)[0].Type));
        Assert.Equal(["c", "d"], Names(doc, Results(doc)[1].Type));
        Assert.False(Field(doc, "a").Nullable);
        Assert.Equal("-2147483648", Scalar(Field(doc, "a")).Facets?.Min?.Value);
        Assert.True(Field(doc, "b").Nullable);
        Assert.False(Field(doc, "c", 1).Nullable);
        Assert.True(Field(doc, "d", 1).Nullable);
    }

    [Theory]
    [InlineData("AS OBJECT dbo.v", "view")]
    [InlineData("AS OBJECT dbo.computed", "computed")]
    [InlineData("AS OBJECT shape", "schema")]
    [InlineData("AS FOR XML", "FOR XML")]
    public void A_declared_set_whose_columns_are_not_modelled_is_unknown_and_the_rest_still_typed(string set, string why)
    {
        var doc = Infer(
            ("shapes", "CREATE TABLE dbo.computed (a int NOT NULL, b AS a + 1);\nGO\nCREATE VIEW dbo.v AS SELECT t.id FROM dbo.t AS t;\nGO\nCREATE TABLE dbo.shape (a int NOT NULL);"),
            ("q", "CREATE PROCEDURE dbo.q AS SELECT t.id FROM dbo.t AS t; SELECT t.id FROM dbo.t AS t;"),
            ("p", $"CREATE PROCEDURE dbo.p AS EXEC dbo.q WITH RESULT SETS ({set}, (two int NOT NULL));"));

        var results = Results(doc);
        Assert.Equal(2, results.Count);
        var unknown = Assert.Single(doc.Unknowns, u => u.Object == "dbo.p");
        Assert.Contains(why, unknown.Detail, StringComparison.Ordinal);
        Assert.Equal(["two"], Names(doc, results[1].Type));
    }

    [Fact]
    public void Insert_exec_with_result_sets_is_a_syntax_error_as_the_server_says()
    {
        // Observed: Insert_exec_refuses_with_result_sets_but_a_callee_may_use_it (102).
        using var dir = new TempDirectory();
        dir.File("p.sql", "CREATE PROCEDURE dbo.p AS CREATE TABLE #r (id int); INSERT INTO #r EXEC dbo.q WITH RESULT SETS ((id int));");

        var refused = Assert.Single(Haruspex.Cli.SqlProject.Load(dir.Path).Diagnostics);
        Assert.Equal("HAR1001", refused.Code.ToString());
        Assert.Contains("RESULT", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Insert_exec_of_a_procedure_that_declares_its_call_s_sets_inserts_the_declared_columns()
    {
        // Observed in the same test: the rows inserted are the converted ones, so the JSON the callee built is not traced
        // through the declared column, which claims only its type.
        var doc = Infer(
            ("q", "CREATE PROCEDURE dbo.q AS SELECT (SELECT t.id FROM dbo.t AS t FOR JSON PATH) AS n;"),
            ("r", "CREATE PROCEDURE dbo.r AS EXEC dbo.q WITH RESULT SETS ((n nvarchar(max) NOT NULL));"),
            ("p", "CREATE PROCEDURE dbo.p AS DECLARE @r TABLE (n nvarchar(max)); INSERT INTO @r EXEC dbo.r; SELECT json_query(x.n) AS j FROM @r AS x;"));

        Assert.IsType<IrUnknownType>(Field(doc, "j").Type);
    }

    [Theory]
    [InlineData("n varchar(3)", "SELECT t.id AS n FROM dbo.t AS t", "it is written *")]
    [InlineData("n nvarchar(5)", "SELECT t.name AS n FROM dbo.t AS t", "cut short")]
    [InlineData("n tinyint", "SELECT t.id AS n FROM dbo.t AS t", "the call fails (8114)")]
    [InlineData("n int NOT NULL", "SELECT t.qty AS n FROM dbo.item AS t", "11553")]
    [InlineData("n date", "SELECT t.id AS n FROM dbo.t AS t", "never converts")]
    [InlineData("n varbinary(2)", "SELECT CONVERT(varbinary(5), t.id) AS n FROM dbo.t AS t", "cut short")]
    [InlineData("n nvarchar(3)", "SELECT t.id AS n FROM dbo.t AS t", "the call fails (8114)")]
    [InlineData("n varchar(3)", "SELECT CONVERT(bigint, t.id) AS n FROM dbo.t AS t", "the call fails (8114)")]
    public void A_declared_column_narrower_than_the_callee_s_is_warned_of(string column, string query, string consequence)
    {
        var run = Run(
            ("q", $"CREATE PROCEDURE dbo.q AS {query};"),
            ("p", $"CREATE PROCEDURE dbo.p AS EXEC dbo.q WITH RESULT SETS (({column}));"));

        var warning = Assert.Single(run.Diagnostics);
        Assert.Equal("HAR2104", warning.Code.ToString());
        Assert.Equal(DiagnosticSeverity.Warning, warning.Code.Severity);
        Assert.Contains(consequence, warning.Message, StringComparison.Ordinal);
        Assert.StartsWith("p.sql:1:", warning.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("n bigint NOT NULL", "SELECT t.id AS n FROM dbo.t AS t")]
    [InlineData("n varchar(11)", "SELECT t.id AS n FROM dbo.t AS t")]
    [InlineData("n nvarchar(10)", "SELECT t.name AS n FROM dbo.t AS t")]
    [InlineData("n nvarchar(max) NULL", "SELECT t.name AS n FROM dbo.t AS t")]
    [InlineData("n decimal(12,2)", "SELECT t.id AS n FROM dbo.t AS t")]
    public void A_declared_column_that_holds_every_value_of_the_callee_s_is_not_warned_of(string column, string query)
    {
        var run = Run(
            ("q", $"CREATE PROCEDURE dbo.q AS {query};"),
            ("p", $"CREATE PROCEDURE dbo.p AS EXEC dbo.q WITH RESULT SETS (({column}));"));

        Assert.Empty(run.Diagnostics);
    }
}
