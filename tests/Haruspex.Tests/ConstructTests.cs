using Haruspex.Emit.Zod;
using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests;

/// <summary>
/// One focused test per construct the front end recognises or refuses. Each refusal is a soundness rule:
/// the test proves the construct becomes an unknown with its reason, rather than a type that could be wrong.
/// </summary>
public class ConstructTests
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

    private static IReadOnlyList<IrField> RowFields(IrDocument doc, int result = 0)
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
    [InlineData("SELECT * FROM dbo.t AS t;")]
    [InlineData("SELECT t.* FROM dbo.t AS t FOR JSON PATH;")]
    public void A_query_form_not_modelled_makes_the_result_unknown(string body) =>
        AssertUnknown(Procedure(body), "results[0]", IrUnknownReasons.UnsupportedQuery);

    [Theory]
    [InlineData("SELECT (t.id) FROM dbo.t AS t;")]
    [InlineData("SELECT (t.id) FROM dbo.t AS t FOR JSON PATH;")]
    public void An_unnamed_column_makes_the_result_unknown(string body) =>
        AssertUnknown(Procedure(body), "results[0]", IrUnknownReasons.UnnamedColumn);

    [Fact]
    public void A_repeated_rowset_column_name_makes_the_result_unknown() =>
        AssertUnknown(
            Procedure("SELECT t.id, u.id FROM dbo.t AS t JOIN dbo.u AS u ON u.t_id = t.id;"),
            "results[0]",
            IrUnknownReasons.DuplicateColumn);

    [Theory]
    [InlineData("t.id AS a, t.code AS [a.b]")]
    [InlineData("t.id AS [a.x], t.code AS b, t.name AS [a.y]")]
    [InlineData("t.id AS a, t.code AS A")]
    [InlineData("t.id AS [a.x], t.code AS [A.y]")]
    public void Clashing_for_json_keys_make_the_result_unknown(string columns) =>
        AssertUnknown(Procedure($"SELECT {columns} FROM dbo.t AS t FOR JSON PATH;"), "results[0]", IrUnknownReasons.DuplicateColumn);

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
        Assert.Equal(IrNullRules.AllFieldsNull, Assert.Single(a.NullableBecause).Rule);
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
        AssertUnknown(Procedure($"SELECT t.id AS {alias} FROM dbo.t AS t FOR JSON PATH;"), "results[0]", IrUnknownReasons.UnsupportedQuery);

    // ---- results ------------------------------------------------------------------------------------

    [Fact]
    public void A_for_json_result_may_be_absent_because_no_rows_return_no_row_at_all()
    {
        var result = Assert.Single(Procedure("SELECT t.id FROM dbo.t AS t FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;").Objects[0].Results!);

        Assert.True(result.MayBeAbsent);
        Assert.Equal(IrNullRules.NoRows, Assert.Single(result.AbsentBecause).Rule);
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
        var ts = ZodEmitter.Emit(Procedure("SELECT t.id FROM dbo.t AS t FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;"));

        Assert.Contains("export const PSchema = PRowSchema.nullable()\n", ts, StringComparison.Ordinal);
    }

    [Fact]
    public void Several_result_sets_are_a_tuple_with_numbered_names()
    {
        var doc = Procedure("SELECT t.id FROM dbo.t AS t;\nSELECT u.name FROM dbo.u AS u FOR JSON PATH;");

        Assert.Equal(["PResult1Row", "PResult2Row"], doc.Types.Keys.Order(StringComparer.Ordinal));
        Assert.Contains(
            "export const PSchema = z.tuple([z.array(PResult1RowSchema), z.array(PResult2RowSchema)])\n",
            ZodEmitter.Emit(doc),
            StringComparison.Ordinal);
    }

    // ---- sources and joins --------------------------------------------------------------------------

    // FULL OUTER JOIN and CROSS JOIN are modelled (SetJoinTests); RIGHT JOIN is not.
    [Theory]
    [InlineData("dbo.t AS t RIGHT JOIN dbo.u AS u ON u.t_id = t.id")]
    [InlineData("dbo.t AS t RIGHT OUTER JOIN dbo.u AS u ON u.t_id = t.id")]
    public void A_join_not_modelled_makes_columns_from_both_sides_unknown(string from)
    {
        var doc = Procedure($"SELECT t.id, u.name FROM {from};");

        AssertUnknown(doc, "results[0][].id", IrUnknownReasons.UnsupportedSource);
        AssertUnknown(doc, "results[0][].name", IrUnknownReasons.UnsupportedSource);
    }

    [Theory]
    [InlineData("STRING_SPLIT(N'a,b', N',') AS s")]
    [InlineData("otherdb.dbo.t AS s")]
    [InlineData("otherdb.dbo.f(1) AS s")]
    public void A_source_not_modelled_makes_its_columns_unknown(string from) =>
        AssertUnknown(Procedure($"SELECT s.id FROM {from};"), "results[0][].id", IrUnknownReasons.UnsupportedSource);

    [Fact]
    public void A_function_the_project_does_not_define_is_unresolved() =>
        AssertUnknown(Procedure("SELECT s.id FROM dbo.f(1) AS s;"), "results[0][].id", IrUnknownReasons.UnresolvedTable);

    [Fact]
    public void An_ambiguous_unqualified_column_is_unknown()
    {
        var unknown = Unknown(Procedure("SELECT name FROM dbo.t AS t JOIN dbo.u AS u ON u.t_id = t.id;"), "results[0][].name");

        Assert.Equal(IrUnknownReasons.UnresolvedColumn, unknown.Reason);
        Assert.Contains("ambiguous", unknown.Detail, StringComparison.Ordinal);
    }

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

        AssertUnknown(doc, "results[0][].name", IrUnknownReasons.UnresolvedTable);
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

        AssertUnknown(doc, "results[0][].name", IrUnknownReasons.UnresolvedTable);
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
        Assert.Equal(IrNullRules.OuterJoin, Assert.Single(fields[1].NullableBecause).Rule);
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
        AssertUnknown(doc, "results", IrUnknownReasons.UnsupportedStatement);
    }

    [Fact]
    public void A_clr_procedure_has_unknown_results()
    {
        var doc = Infer(("p.sql", "CREATE PROCEDURE dbo.clr @x int AS EXTERNAL NAME asm.[Ns.Cls].Method;"));

        Assert.Null(doc.Objects[0].Results);
        AssertUnknown(doc, "results", IrUnknownReasons.ExternalCode);
    }

    // ---- types --------------------------------------------------------------------------------------

    [Fact]
    public void A_computed_column_is_unknown() =>
        AssertUnknown(Procedure("SELECT c.b FROM dbo.c AS c;"), "results[0][].b", IrUnknownReasons.UnsupportedSqlType);

    // ---- statements outside procedures --------------------------------------------------------------

    [Theory]
    [InlineData("ALTER TABLE dbo.t ALTER COLUMN code int NULL;")]
    [InlineData("ALTER TABLE dbo.t ADD extra int NULL;")]
    [InlineData("ALTER TABLE dbo.t DROP COLUMN code;")]
    [InlineData("DROP TABLE dbo.t;")]
    public void A_table_changed_after_its_create_is_unknown(string change)
    {
        var doc = Infer(("tables.sql", Tables), ("change.sql", change), ("p.sql", "CREATE PROCEDURE dbo.p AS SELECT t.code FROM dbo.t AS t;"));

        AssertUnknown(doc, "results[0][].code", IrUnknownReasons.UnsupportedSource);
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
        AssertUnknown(doc, "results", IrUnknownReasons.Redefined);
    }

    [Fact]
    public void A_procedure_defined_only_by_alter_has_unknown_results()
    {
        var doc = Infer(("tables.sql", Tables), ("b.sql", "ALTER PROCEDURE dbo.p AS SELECT t.id FROM dbo.t AS t;"));

        Assert.Null(Assert.Single(doc.Objects).Results);
        AssertUnknown(doc, "results", IrUnknownReasons.Redefined);
    }

    // ---- generated code -----------------------------------------------------------------------------

    [Fact]
    public void A_procedure_name_cannot_inject_code_into_the_generated_module()
    {
        var ts = ZodEmitter.Emit(Infer(
            ("tables.sql", Tables),
            ("p.sql", "CREATE PROCEDURE dbo.[x*/ export const injected = 1; /*] AS SELECT t.id FROM dbo.t AS t;")));

        Assert.DoesNotContain("*/ export const injected", ts, StringComparison.Ordinal);
    }
}
