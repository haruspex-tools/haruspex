using System.Globalization;
using Haruspex.Collections;
using Haruspex.Emit.Zod;
using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

/// <summary>
/// One focused test per construct the front end recognises or refuses. Each refusal is a soundness rule:
/// the test proves the construct becomes an unknown with its reason, rather than a type that could be wrong.
/// </summary>
public partial class ConstructTests
{
    private const string Tables = """
        CREATE TABLE dbo.t (id int NOT NULL PRIMARY KEY, name nvarchar(10) NULL, code int NOT NULL);
        GO
        CREATE TABLE dbo.u (id int NOT NULL PRIMARY KEY, t_id int NOT NULL, name nvarchar(10) NOT NULL);
        GO
        CREATE TABLE dbo.c (a int NOT NULL, b AS a * 2);
        GO
        """;

    private static IrDocument Infer(params (string File, string Sql)[] files) => InferProcedure.Infer("*.*", files);

    /// <summary>Infers <c>dbo.p</c>, whose body is <paramref name="body"/>, over the standard tables.</summary>
    private static IrDocument Procedure(string body) => Infer(("tables.sql", Tables), ("p.sql", $"CREATE PROCEDURE dbo.p AS\n{body}"));

    private static EquatableArray<IrField> RowFields(IrDocument doc, int result = 0)
    {
        var results = Assert.IsType<IReadOnlyList<IrResult>>(doc.Objects[0].Results, exactMatch: false);
        var row = results[result].Type switch
        {
            IrArray { Items: IrRef r } => r,
            IrRef r => r,
            var other => throw new InvalidOperationException($"Unexpected result type {other}."),
        };
        return doc.Types[row.Name].Fields;
    }

    // ---- queries ------------------------------------------------------------------------------------

    [Theory]
    [InlineData("SELECT t.id FROM dbo.t AS t FOR XML PATH;")]
    [InlineData("SELECT t.id FROM dbo.t AS t FOR JSON AUTO;")]
    [InlineData("SELECT t.id FROM dbo.t AS t FOR JSON PATH, ROOT('items');")]
    public void A_query_form_not_modelled_makes_the_result_unknown(string body) =>
        AssertUnknown(Procedure(body), "results[0]", IrUnknownReason.UnsupportedQuery);

    [Theory]
    [InlineData("SELECT (t.id) FROM dbo.t AS t;")]
    [InlineData("SELECT (t.id) FROM dbo.t AS t FOR JSON PATH;")]
    public void An_unnamed_column_makes_the_result_unknown(string body) =>
        AssertUnknown(Procedure(body), "results[0]", IrUnknownReason.UnnamedColumn);

    [Fact]
    public void A_repeated_rowset_column_name_makes_the_result_unknown() =>
        AssertUnknown(
            Procedure("SELECT t.id, u.id FROM dbo.t AS t JOIN dbo.u AS u ON u.t_id = t.id;"),
            "results[0]",
            IrUnknownReason.DuplicateColumn);

    [Theory]
    [InlineData("SELECT t.id AS [JSON_F52E2B61-18A1-11d1-B105-00805F49916B] FROM dbo.t AS t;")]
    [InlineData("SELECT t.name AS [XML_F52E2B61-18A1-11d1-B105-00805F49916B] FROM dbo.t AS t;")]
    [InlineData("SELECT (SELECT u.name FROM dbo.u AS u FOR JSON PATH) AS [JSON_F52E2B61-18A1-11d1-B105-00805F49916B];")]
    [InlineData("UPDATE dbo.t SET code = 1 OUTPUT inserted.id AS [JSON_F52E2B61-18A1-11d1-B105-00805F49916B];")]
    public void A_rowset_whose_one_column_the_driver_takes_for_for_json_or_for_xml_output_is_unknown(string body)
    {
        // MssqlDriverObservationTests: the driver joins such a result set's rows into one, in either mode, and with
        // parseJSON parses a JSON one, so neither the rows nor their values arrive as SQL Server sent them.
        var unknown = Unknown(Procedure(body), "results[0]");

        Assert.Equal(IrUnknownReason.UnsupportedQuery, unknown.Reason);
        Assert.Contains("mssql driver", unknown.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SELECT t.id AS [json_f52e2b61-18a1-11d1-b105-00805f49916b] FROM dbo.t AS t;")]
    [InlineData("SELECT t.id AS [JSON_F52E2B61-18A1-11d1-B105-00805F49916B], t.code FROM dbo.t AS t;")]
    public void The_driver_goes_by_the_exact_name_of_the_one_column_so_others_are_rows(string body) =>
        AllTyped(Procedure(body));

    [Theory]
    [InlineData("t.id AS a, t.code AS [a.b]")]
    [InlineData("t.id AS [a.x], t.code AS b, t.name AS [a.y]")]
    [InlineData("t.id AS [a.x], t.code AS [a.x]")]
    public void Clashing_for_json_keys_make_the_result_unknown(string columns) =>
        AssertUnknown(Procedure($"SELECT {columns} FROM dbo.t AS t FOR JSON PATH;"), "results[0]", IrUnknownReason.DuplicateColumn);

    [Fact]
    public void For_json_keys_that_differ_only_in_case_are_two_keys()
    {
        // Observed: For_json_keys_that_differ_only_in_case_are_two_keys ([{"a":1,"A":2,"b":{"x":3},"B":{"x":4}}]).
        var doc = Procedure("SELECT t.id AS a, t.code AS A, t.id AS [b.x], t.code AS [B.x] FROM dbo.t AS t FOR JSON PATH;");

        Assert.Empty(doc.Unknowns);
        Assert.Equal(["a", "A", "b", "B"], RowFields(doc).Select(f => f.Name));
        Assert.All(RowFields(doc).Skip(2), f => Assert.Equal(["x"], doc.Types[Assert.IsType<IrRef>(f.Type).Name].Fields.Select(g => g.Name)));
    }

    [Fact]
    public void Adjacent_dotted_aliases_nest_in_one_object()
    {
        var doc = Procedure("SELECT t.id AS [a.x], t.code AS [a.y] FROM dbo.t AS t FOR JSON PATH;");

        Assert.Empty(doc.Unknowns);
        var a = Assert.Single(RowFields(doc));
        Assert.Equal(["x", "y"], doc.Types[Assert.IsType<IrRef>(a.Type).Name].Fields.Select(f => f.Name));
    }

    [Fact]
    public void Without_include_null_values_a_nested_object_whose_values_can_all_be_null_may_be_left_out()
    {
        // Observed in ForJsonObservationTests (differential tests): [{"x":1}] when every value is null.
        var doc = Procedure("SELECT t.id AS x, t.name AS [a.y], t.name AS [a.z] FROM dbo.t AS t FOR JSON PATH;");

        var a = RowFields(doc)[1];
        Assert.Equal(IrPresence.OmittedWhenNull, a.Presence);
        Assert.Equal(IrNullRule.AllFieldsNull, Assert.Single(a.NullableBecause).Rule);
    }

    [Theory]
    [InlineData("t.name AS [a.y], t.name AS [a.z]")]
    [InlineData("t.name AS [a.b.y]")]
    public void With_include_null_values_a_nested_object_is_always_present_and_never_null(string columns)
    {
        // Observed in ForJsonObservationTests (differential tests): [{"x":1,"a":{"y":null,"z":null}}], and
        // {"a":{"b":{"y":null}}} however deep.
        var doc = Procedure($"SELECT t.id AS x, {columns} FROM dbo.t AS t FOR JSON PATH, INCLUDE_NULL_VALUES;");

        var a = RowFields(doc)[1];
        Assert.Equal(IrPresence.Always, a.Presence);
        Assert.False(a.Nullable);
        var inner = doc.Types[Assert.IsType<IrRef>(a.Type).Name].Fields[0];
        Assert.Equal(IrPresence.Always, inner.Presence);
        Assert.Equal(inner.Type is IrRef, !inner.Nullable);
    }

    [Theory]
    [InlineData("[a..b]")]
    [InlineData("[.a]")]
    [InlineData("[a.]")]
    public void An_invalid_for_json_path_makes_the_result_unknown(string alias) =>
        AssertUnknown(Procedure($"SELECT t.id AS {alias} FROM dbo.t AS t FOR JSON PATH;"), "results[0]", IrUnknownReason.UnsupportedQuery);

    // ---- results ------------------------------------------------------------------------------------

    [Fact]
    public void A_for_json_result_may_be_absent_because_no_rows_return_no_row_at_all()
    {
        var result = Assert.Single(Procedure("SELECT t.id FROM dbo.t AS t FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;").Objects[0].Results!);

        Assert.True(result.MayBeAbsent);
        Assert.Equal(IrNullRule.NoRows, Assert.Single(result.AbsentBecause).Rule);
    }

    [Fact]
    public void A_rowset_result_is_always_present()
    {
        var result = Assert.Single(Procedure("SELECT t.id FROM dbo.t AS t;").Objects[0].Results!);

        Assert.False(result.MayBeAbsent);
    }

    [Fact]
    public void An_absent_single_object_result_is_nullable_in_zod()
    {
        var ts = ZodEmitter.Emit(Procedure("SELECT t.id FROM dbo.t AS t FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;")).Expected();

        Assert.Contains("export const PSchema = PRowSchema.nullable()\n", ts, StringComparison.Ordinal);
    }

    [Fact]
    public void Several_result_sets_are_a_tuple_with_numbered_names()
    {
        var doc = Procedure("SELECT t.id FROM dbo.t AS t;\nSELECT u.name FROM dbo.u AS u FOR JSON PATH;");

        Assert.Equal(["PResult1Row", "PResult2Row"], doc.Types.Keys.Order(StringComparer.Ordinal));
        Assert.Contains(
            "export const PSchema = z.tuple([z.array(PResult1RowSchema), z.array(PResult2RowSchema).min(1, { error: \"Expected at least one row\" }).nullable()])\n",
            ZodEmitter.Emit(doc).Expected(),
            StringComparison.Ordinal);
    }

    // ---- sources and joins --------------------------------------------------------------------------

    [Theory]
    [InlineData("REGEXP_SPLIT_TO_TABLE(N'a,b', N',') AS s")]
    [InlineData("otherdb.dbo.t AS s")]
    [InlineData("otherdb.dbo.f(1) AS s")]
    public void A_source_not_modelled_makes_its_columns_unknown(string from) =>
        AssertUnknown(Procedure($"SELECT s.id FROM {from};"), "results[0][].id", IrUnknownReason.UnsupportedSource);

    [Fact]
    public void A_function_the_project_does_not_define_is_unresolved() =>
        AssertUnknown(Procedure("SELECT s.id FROM dbo.f(1) AS s;"), "results[0][].id", IrUnknownReason.UnresolvedTable);

    [Fact]
    public void An_ambiguous_unqualified_column_is_unknown()
    {
        var unknown = Unknown(Procedure("SELECT name FROM dbo.t AS t JOIN dbo.u AS u ON u.t_id = t.id;"), "results[0][].name");

        Assert.Equal(IrUnknownReason.UnresolvedColumn, unknown.Reason);
        Assert.Contains("ambiguous", unknown.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SELECT t.nope AS c FROM dbo.t AS t;")]
    [InlineData("SELECT nope AS c FROM dbo.t AS t;")]
    [InlineData("WITH w AS (SELECT t.id FROM dbo.t AS t) SELECT w.nope AS c FROM w;")]
    public void A_column_no_source_has_is_unknown(string body) =>
        AssertUnknown(Procedure(body), "results[0][].c", IrUnknownReason.UnresolvedColumn);

    [Theory]
    [InlineData("SELECT (SELECT TOP (1) name FROM #callers AS c) AS n FROM dbo.t AS t;", IrUnknownReason.CallerTempTable)]
    [InlineData("SELECT (SELECT TOP (1) name FROM (SELECT * FROM #callers AS c) AS d) AS n FROM dbo.t AS t;", IrUnknownReason.CallerTempTable)]
    [InlineData("SELECT (SELECT TOP (1) name FROM dbo.nope AS x) AS n FROM dbo.t AS t;", IrUnknownReason.UnresolvedTable)]
    public void A_column_an_inner_source_of_unknown_columns_may_have_is_unknown_not_the_outer_query_s(string body, IrUnknownReason reason) =>
        AssertUnknown(Procedure(body), "results[0][].n", reason);

    [Fact]
    public void A_column_reference_of_three_parts_is_typed_as_one_through_an_alias()
    {
        // Binding only: once bound, dbo.t.name is t.name (BinderTests.FullNames.cs has the rules).
        var full = Procedure("SELECT dbo.t.id, dbo.t.name FROM dbo.t JOIN dbo.u AS u ON u.t_id = dbo.t.id WHERE dbo.t.name IS NOT NULL ORDER BY dbo.t.code;");
        var aliased = Procedure("SELECT t.id, t.name FROM dbo.t AS t JOIN dbo.u AS u ON u.t_id = t.id WHERE t.name IS NOT NULL ORDER BY t.code;");

        Assert.Equal(RowFields(aliased), RowFields(full));
        Assert.False(RowFields(full)[1].Nullable);
    }

    [Fact]
    public void A_table_variable_has_no_full_name() =>
        // Observed: A_table_variable_has_no_full_name (4104 for the bracketed form; 102 without brackets).
        AssertUnknown(Procedure("DECLARE @v TABLE (c int NOT NULL);\nSELECT dbo.[@v].c AS c FROM @v;"), "results[0][].c", IrUnknownReason.UnresolvedColumn);

    [Fact]
    public void A_column_reference_of_four_parts_is_unknown() =>
        AssertUnknown(Procedure("SELECT here.dbo.t.id AS c FROM dbo.t;"), "results[0][].c", IrUnknownReason.UnsupportedExpression);

    [Fact]
    public void An_unqualified_column_resolves_when_only_one_table_has_it()
    {
        var doc = Procedure("SELECT code FROM dbo.t AS t JOIN dbo.u AS u ON u.t_id = t.id;");

        Assert.Empty(doc.Unknowns);
        Assert.False(Assert.Single(RowFields(doc)).Nullable);
    }

    [Fact]
    public void An_unqualified_table_resolves_when_only_one_schema_has_it()
    {
        var doc = Infer(
            ("tables.sql", "CREATE TABLE sales.customer (name nvarchar(10) NOT NULL);"),
            ("p.sql", "CREATE PROCEDURE sales.p AS SELECT c.name FROM customer AS c;"));

        Assert.Empty(doc.Unknowns);
        Assert.False(Assert.Single(RowFields(doc)).Nullable);
    }

    [Fact]
    public void An_unqualified_table_in_more_than_one_schema_is_unresolved()
    {
        // Which table SQL Server reads depends on the caller's default schema, which the source cannot tell.
        var doc = Infer(
            ("tables.sql", "CREATE TABLE dbo.customer (name nvarchar(10) NOT NULL);\nGO\nCREATE TABLE sales.customer (name nvarchar(10) NULL);"),
            ("p.sql", "CREATE PROCEDURE sales.p AS SELECT c.name FROM customer AS c;"));

        AssertUnknown(doc, "results[0][].name", IrUnknownReason.UnresolvedTable);
    }

    [Theory]
    [InlineData("CREATE TABLE dbo.customer_base (name nvarchar(10) NULL);\nGO\nCREATE VIEW dbo.customer AS SELECT b.name FROM dbo.customer_base AS b;")]
    [InlineData("CREATE TABLE dbo.other (name nvarchar(10) NULL);\nGO\nCREATE SYNONYM dbo.customer FOR dbo.other;")]
    [InlineData("CREATE FUNCTION dbo.customer() RETURNS TABLE AS RETURN SELECT CAST(NULL AS nvarchar(10)) AS name;")]
    public void An_unqualified_table_with_a_rival_view_synonym_or_function_is_unresolved(string rival)
    {
        // A caller whose default schema is dbo reads the rival, which is not modelled, so nothing is claimed.
        var doc = Infer(
            ("tables.sql", "CREATE TABLE sales.customer (name nvarchar(10) NOT NULL);"),
            ("rival.sql", rival),
            ("p.sql", "CREATE PROCEDURE sales.p AS SELECT c.name FROM customer AS c;"));

        AssertUnknown(doc, "results[0][].name", IrUnknownReason.UnresolvedTable);
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("dbo.customer")]
    public void A_view_read_by_name_is_typed_from_its_definition(string reference)
    {
        // A view of the project is read like a derived table (ApplyViewTests has the rules).
        var doc = Infer(
            ("tables.sql", "CREATE TABLE dbo.customer_base (name nvarchar(10) NOT NULL);\nGO\nCREATE VIEW dbo.customer AS SELECT b.name FROM dbo.customer_base AS b;"),
            ("p.sql", $"CREATE PROCEDURE dbo.p AS SELECT c.name FROM {reference} AS c;"));

        Assert.Empty(doc.Unknowns);
        Assert.False(Assert.Single(RowFields(doc)).Nullable);
    }

    [Fact]
    public void A_left_join_makes_the_optional_side_nullable()
    {
        var fields = RowFields(Procedure("SELECT t.id, u.name FROM dbo.t AS t LEFT JOIN dbo.u AS u ON u.t_id = t.id;"));

        Assert.False(fields[0].Nullable);
        Assert.Equal(IrNullRule.UnmatchedRow, Assert.Single(fields[1].NullableBecause).Rule);
    }

    // ---- statements ---------------------------------------------------------------------------------

    [Theory]
    [InlineData("SELECT t.id INTO #x FROM dbo.t AS t;")]
    [InlineData("DECLARE @n int;\nSELECT @n = t.id FROM dbo.t AS t;")]
    public void A_select_that_returns_no_result_set_adds_none(string first)
    {
        var doc = Procedure($"{first}\nSELECT t.code FROM dbo.t AS t;");

        Assert.Equal("code", Assert.Single(RowFields(doc)).Name);
        Assert.Single(doc.Objects[0].Results!);
    }

    [Theory]
    [InlineData("SET NOCOUNT ON;")]
    [InlineData("SET NOCOUNT, XACT_ABORT ON;")]
    [InlineData("SET XACT_ABORT OFF;")]
    [InlineData("SET ANSI_NULLS ON;")]
    [InlineData("SET QUOTED_IDENTIFIER ON;")]
    public void A_harmless_set_option_does_not_affect_the_results(string set) =>
        Assert.Single(Procedure($"{set}\nSELECT t.id FROM dbo.t AS t;").Objects[0].Results!);

    [Theory]
    [InlineData("SET NOEXEC ON;")]
    [InlineData("SET FMTONLY ON;")]
    [InlineData("SET PARSEONLY ON;")]
    [InlineData("SET ANSI_WARNINGS OFF;")]
    [InlineData("SET CONCAT_NULL_YIELDS_NULL OFF;")]
    [InlineData("SET NOCOUNT, NOEXEC ON;")]
    public void Any_other_set_option_makes_the_results_unknown(string set)
    {
        var doc = Procedure($"{set}\nSELECT t.id FROM dbo.t AS t;");

        Assert.Null(doc.Objects[0].Results);
        AssertUnknown(doc, "results", IrUnknownReason.UnsupportedStatement);
    }

    [Fact]
    public void A_clr_procedure_has_unknown_results()
    {
        var doc = Infer(("p.sql", "CREATE PROCEDURE dbo.clr @x int AS EXTERNAL NAME asm.[Ns.Cls].Method;"));

        Assert.Null(doc.Objects[0].Results);
        AssertUnknown(doc, "results", IrUnknownReason.ExternalCode);
    }

    // ---- types --------------------------------------------------------------------------------------

    [Fact]
    public void A_computed_column_is_read_as_its_expression() =>
        // ExpressionTests.ComputedColumns.cs holds what it gives.
        Assert.Empty(Procedure("SELECT c.b FROM dbo.c AS c;").Unknowns);

    // ---- statements outside procedures --------------------------------------------------------------

    [Theory]
    [InlineData("ALTER TABLE dbo.t ALTER COLUMN code int NULL;")]
    [InlineData("ALTER TABLE dbo.t ADD extra int NULL;")]
    [InlineData("ALTER TABLE dbo.t DROP COLUMN code;")]
    [InlineData("DROP TABLE dbo.t;")]
    public void A_table_changed_after_its_create_is_unknown(string change)
    {
        var doc = Infer(("tables.sql", Tables), ("change.sql", change), ("p.sql", "CREATE PROCEDURE dbo.p AS SELECT t.code FROM dbo.t AS t;"));

        AssertUnknown(doc, "results[0][].code", IrUnknownReason.UnsupportedSource);
    }

    /// <summary>
    /// The places a statement may stand without being a batch's own, each with <c>{0}</c> for the statement: the guarded form an
    /// idempotent migration takes, a block, <c>TRY</c>, a loop and a procedure's body.
    /// </summary>
    private static readonly string[] Places =
    [
        "IF COL_LENGTH('dbo.t', 'code') IS NOT NULL {0}",
        "BEGIN {0} END",
        "BEGIN TRY {0} END TRY BEGIN CATCH END CATCH",
        "WHILE 1 = 0 {0}",
        "CREATE PROCEDURE dbo.migrate AS BEGIN {0} END",
    ];

    /// <summary>Each statement that changes, drops or creates the table again, in each place it may stand.</summary>
    public static TheoryData<string, string> NestedChanges { get; } = [.. Places.SelectMany(nesting => ((string[])[
        "ALTER TABLE dbo.t ALTER COLUMN code int NULL;",
        "DROP TABLE dbo.t;",
        "CREATE TABLE dbo.t (id int NULL, code varchar(10) NULL);"]).Select(change => (nesting, change)))];

    [Theory]
    [MemberData(nameof(NestedChanges))]
    public void A_table_changed_dropped_or_created_again_at_any_depth_is_unknown(string nesting, string change)
    {
        // Whether the statement runs isn't knowable, so it may (observed: A_column_loosened_under_if_holds_null), and a second
        // CREATE TABLE may run in place of the first, depending on the order the scripts run.
        var doc = InferProcedure.Infer("dbo.p", ("tables.sql", Tables), ("change.sql", string.Format(CultureInfo.InvariantCulture, nesting, change)), ("p.sql", "CREATE PROCEDURE dbo.p AS SELECT t.code FROM dbo.t AS t;"));

        AssertUnknown(doc, "results[0][].code", IrUnknownReason.UnsupportedSource);
    }

    [Theory]
    // An install script's drop and create: the CREATE TABLE after the drop, in the same script, is the table that stands.
    [InlineData("IF OBJECT_ID(N'dbo.t') IS NOT NULL DROP TABLE dbo.t;\nGO\n", false)]
    [InlineData("DROP TABLE IF EXISTS dbo.t;\nGO\n", false)]
    // In a body the drop may run at any time after the create, and once the table is created again elsewhere, or not at all.
    [InlineData("CREATE PROCEDURE dbo.reset AS DROP TABLE dbo.t;\nGO\n", true)]
    public void A_table_dropped_before_its_create_in_the_same_script_is_its_create(string before, bool unknown)
    {
        var doc = InferProcedure.Infer("dbo.p", ("tables.sql", before + Tables), ("p.sql", "CREATE PROCEDURE dbo.p AS SELECT t.code FROM dbo.t AS t;"));

        Assert.Equal(unknown, doc.Unknowns.Any(u => u.Reason == IrUnknownReason.UnsupportedSource));
    }

    [Theory]
    // Turning versioning off changes no column (observed: Turning_system_versioning_off_changes_no_column), and dropping the
    // period shows a hidden column, so it changes only a table that has one (Dropping_a_period_shows_its_hidden_columns_and_keeps_their_types).
    [InlineData("", false)]
    [InlineData("HIDDEN", true)]
    public void A_period_dropped_in_a_load_procedure_changes_only_a_table_with_a_hidden_column(string hidden, bool unknown)
    {
        var doc = InferProcedure.Infer(
            "dbo.p",
            ("rate.sql", $"CREATE TABLE dbo.rate (id int NOT NULL PRIMARY KEY, amount int NOT NULL, valid_from datetime2 GENERATED ALWAYS AS ROW START {hidden} NOT NULL, valid_to datetime2 GENERATED ALWAYS AS ROW END NOT NULL, PERIOD FOR SYSTEM_TIME (valid_from, valid_to)) WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.rate_history));"),
            ("load.sql", "CREATE PROCEDURE dbo.load AS BEGIN ALTER TABLE dbo.rate SET (SYSTEM_VERSIONING = OFF); ALTER TABLE dbo.rate DROP PERIOD FOR SYSTEM_TIME; END"),
            ("p.sql", "CREATE PROCEDURE dbo.p AS SELECT r.amount FROM dbo.rate AS r;"));

        Assert.Equal(unknown, doc.Unknowns.Any(u => u.Reason == IrUnknownReason.UnsupportedSource));
    }

    [Theory]
    [InlineData("IF 1 = 1 ALTER TABLE dbo.t ADD CONSTRAINT ck_t CHECK (code > 0);")]
    [InlineData("CREATE PROCEDURE dbo.work AS BEGIN CREATE TABLE #t (code int NULL); ALTER TABLE #t ALTER COLUMN code bigint NULL; DROP TABLE #t; END")]
    public void A_nested_statement_that_only_tightens_or_names_a_temporary_table_leaves_the_table_read(string change)
    {
        var doc = InferProcedure.Infer("dbo.p", ("tables.sql", Tables), ("change.sql", change), ("p.sql", "CREATE PROCEDURE dbo.p AS SELECT t.code FROM dbo.t AS t;"));

        Assert.Empty(doc.Unknowns);
        Assert.False(Assert.Single(RowFields(doc)).Nullable);
    }

    [Theory]
    [InlineData("ALTER TABLE dbo.t ADD CONSTRAINT ck_t CHECK (code > 0);")]
    [InlineData("ALTER TABLE dbo.t WITH CHECK CHECK CONSTRAINT ALL;")]
    public void A_constraint_added_after_the_create_is_ignored(string change)
    {
        var doc = Infer(("tables.sql", Tables), ("change.sql", change), ("p.sql", "CREATE PROCEDURE dbo.p AS SELECT t.code FROM dbo.t AS t;"));

        Assert.Empty(doc.Unknowns);
        Assert.False(Assert.Single(RowFields(doc)).Nullable);
    }

    [Theory]
    [InlineData("ALTER PROCEDURE dbo.p AS SELECT t.name AS id FROM dbo.t AS t;")]
    [InlineData("DROP PROCEDURE dbo.p;")]
    public void A_procedure_changed_after_its_create_has_unknown_results(string change)
    {
        var doc = Infer(("tables.sql", Tables), ("a.sql", "CREATE PROCEDURE dbo.p AS SELECT t.id FROM dbo.t AS t;"), ("b.sql", change));

        Assert.Null(Assert.Single(doc.Objects).Results);
        AssertUnknown(doc, "results", IrUnknownReason.Redefined);
    }

    [Fact]
    public void A_procedure_defined_only_by_one_alter_is_that_alter_and_by_two_has_unknown_results()
    {
        // Its stub is made where the reader can't see, by dynamic SQL; the ALTER replaces whatever stood
        // (docs/decisions/0080-the-last-definition-in-a-script-stands.md). Two scripts' ALTERs run in an order the folder doesn't give.
        var one = Infer(("tables.sql", Tables), ("b.sql", "ALTER PROCEDURE dbo.p AS SELECT t.id FROM dbo.t AS t;"));
        var two = Infer(("tables.sql", Tables), ("b.sql", "ALTER PROCEDURE dbo.p AS SELECT t.id FROM dbo.t AS t;"), ("c.sql", "ALTER PROCEDURE dbo.p AS SELECT t.name FROM dbo.t AS t;"));

        Assert.Empty(one.Unknowns);
        Assert.False(Assert.Single(RowFields(one)).Nullable);
        Assert.Null(Assert.Single(two.Objects).Results);
        AssertUnknown(two, "results", IrUnknownReason.Redefined);
    }

    // ---- generated code -----------------------------------------------------------------------------

    [Fact]
    public void A_procedure_name_cannot_inject_code_into_the_generated_module()
    {
        var ts = ZodEmitter.Emit(Infer(
            ("tables.sql", Tables),
            ("p.sql", "CREATE PROCEDURE dbo.[x*/ export const injected = 1; /*] AS SELECT t.id FROM dbo.t AS t;"))).Expected();

        Assert.DoesNotContain("*/ export const injected", ts, StringComparison.Ordinal);
    }
}
