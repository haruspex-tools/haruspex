using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

/// <summary>
/// Table-valued functions as table sources: inline ones typed from their body with the call's arguments bound to
/// the parameters, multi-statement ones from their declared return table. The engine rules are observed in the differential
/// tests' <c>TableFunctionObservationTests</c>, named where they matter.
/// </summary>
public class TableFunctionTests
{
    private const string Schema = """
        CREATE TABLE dbo.t (id int NOT NULL PRIMARY KEY, name nvarchar(10) NULL, n int NULL, code varchar(10) NOT NULL);
        GO
        CREATE TABLE dbo.u (id int NOT NULL PRIMARY KEY, t_id int NOT NULL, qty int NULL, price decimal(10,2) NOT NULL);
        GO
        CREATE VIEW dbo.tv AS SELECT t.id, t.name FROM dbo.t AS t;
        GO
        CREATE FUNCTION dbo.t_by_id (@id int) RETURNS TABLE AS RETURN SELECT t.id, t.name, t.code FROM dbo.t AS t WHERE t.id = @id;
        GO
        CREATE FUNCTION dbo.echo (@x int, @s varchar(3)) RETURNS TABLE AS RETURN SELECT @x AS x, @s AS s;
        GO
        CREATE FUNCTION dbo.tiny (@x tinyint) RETURNS TABLE AS RETURN SELECT @x AS x;
        GO
        CREATE FUNCTION dbo.u_totals (@t_id int) RETURNS TABLE AS RETURN
            SELECT COUNT_BIG(*) AS lines, COALESCE(SUM(u.price), 0) AS total FROM dbo.u AS u WHERE u.t_id = @t_id;
        GO
        CREATE FUNCTION dbo.u_lines (@t_id int) RETURNS TABLE AS RETURN SELECT u.id, u.qty FROM dbo.u AS u WHERE u.t_id = @t_id;
        GO
        CREATE FUNCTION dbo.via_view (@id int) RETURNS TABLE AS RETURN SELECT v.id, v.name FROM dbo.tv AS v WHERE v.id = @id;
        GO
        CREATE FUNCTION dbo.dirty (@id int) RETURNS TABLE AS RETURN SELECT t.id FROM dbo.t AS t WITH (NOLOCK) WHERE t.id = @id;
        GO
        CREATE FUNCTION dbo.with_default (@x int = 5) RETURNS TABLE AS RETURN SELECT @x AS x;
        GO
        CREATE FUNCTION dbo.both (@a int) RETURNS TABLE AS RETURN SELECT @a AS a UNION ALL SELECT @a;
        GO
        CREATE FUNCTION dbo.ms (@n int) RETURNS @r TABLE (id int NOT NULL PRIMARY KEY, label nvarchar(10) NULL, code varchar(3) NOT NULL UNIQUE,
            score int NULL CHECK (score BETWEEN 1 AND 5)) AS
        BEGIN
            INSERT @r (id, label, code) SELECT t.id, t.name, LEFT(t.code, 3) FROM dbo.t AS t WHERE t.n = @n;
            RETURN;
        END
        GO
        CREATE FUNCTION dbo.scalar (@x int) RETURNS int AS BEGIN RETURN @x; END
        GO
        CREATE FUNCTION dbo.names_json () RETURNS nvarchar(max) AS BEGIN RETURN (SELECT v.id, v.name FROM dbo.tv AS v FOR JSON PATH); END
        GO
        """;

    private static IrDocument Procedure(string body, string parameters = "@id int") =>
        InferProcedure.Infer("dbo.p", ("schema.sql", Schema), ("p.sql", $"CREATE PROCEDURE dbo.p {parameters} AS\n{body}"));

    private static IrResult Result(IrDocument doc) =>
        Assert.Single(doc.Objects[0].Results ?? throw new InvalidOperationException("Unknown results: " + string.Join("; ", doc.Unknowns.Select(u => u.Detail))));

    // Stricter than IrNavigation.Field: asserts the procedure has one result, an array of rows.
    private static IrField Field(IrDocument doc, string name)
    {
        var row = (IrRef)((IrArray)Result(doc).Type).Items;
        return Assert.Single(doc.Types[row.Name].Fields, f => f.Name == name);
    }

    /// <summary>The field's SQL type and its reasons to be null, or <c>unknown: reason</c>.</summary>
    private static string Typed(IrDocument doc, string name)
    {
        var field = Field(doc, name);
        var type = field.Type is IrScalar scalar
            ? scalar.Sql ?? "(none)"
            : "unknown: " + string.Join("; ", doc.Unknowns.Where(u => u.Path.EndsWith("." + name, StringComparison.Ordinal)).Select(u => IrWireNames.Of(u.Reason)));
        return string.Join(" ", [type, .. field.NullableBecause.Select(r => IrWireNames.Of(r.Rule))]);
    }

    // ---- inline functions -------------------------------------------------------------------------------------------

    [Fact]
    public void An_inline_function_is_typed_from_its_body_with_its_keys()
    {
        var doc = Procedure("SELECT f.id, f.name, f.code FROM dbo.t_by_id(@id) AS f;");
        AllTyped(doc);
        Assert.Equal("int", Typed(doc, "id"));
        Assert.Equal("nvarchar(10) nullable-column", Typed(doc, "name"));
        Assert.Equal("varchar(10)", Typed(doc, "code"));
        Assert.Equal(IrCardinality.AtMostOne, Result(doc).Cardinality);
    }

    [Fact]
    public void An_argument_never_null_is_never_null_where_the_body_passes_it_through()
    {
        // An_argument_is_converted_to_its_parameters_type_as_an_assignment_is: a string is cut short, an overflow is NULL under
        // the caller's ANSI_WARNINGS and ARITHABORT off.
        var doc = Procedure("SELECT t.id, e.x, e.s, y.x AS tiny FROM dbo.t AS t CROSS APPLY dbo.echo(t.n, t.code) AS e CROSS APPLY dbo.tiny(t.id) AS y;");
        AllTyped(doc);
        Assert.Equal("int nullable-column", Typed(doc, "x"));
        Assert.Equal("varchar(3)", Typed(doc, "s"));
        Assert.Equal("tinyint arithmetic-overflow", Typed(doc, "tiny"));

        var literal = Procedure("SELECT e.x, e.s FROM dbo.echo(1, 'abc') AS e;");
        Assert.Equal("int", Typed(literal, "x"));
        Assert.Equal("varchar(3)", Typed(literal, "s"));
    }

    [Fact]
    public void A_function_per_outer_row_keeps_the_outer_bound()
    {
        // An ungrouped aggregate is one row for every outer row (An_ungrouped_aggregate_applied_gives_one_row_for_every_outer_row),
        // so OUTER APPLY never null-extends it, and the lookup by key stays at most one row.
        var doc = Procedure("SELECT t.id, s.lines, s.total FROM dbo.t AS t OUTER APPLY dbo.u_totals(t.id) AS s WHERE t.id = @id;");
        AllTyped(doc);
        Assert.Equal("bigint", Typed(doc, "lines"));
        Assert.Equal("decimal(38,2)", Typed(doc, "total"));
        Assert.Equal(IrCardinality.AtMostOne, Result(doc).Cardinality);
    }

    [Fact]
    public void A_function_of_many_rows_applied_is_many_and_outer_apply_makes_it_nullable()
    {
        var doc = Procedure("SELECT t.id, l.id AS line_id, l.qty FROM dbo.t AS t OUTER APPLY dbo.u_lines(t.id) AS l WHERE t.id = @id;");
        Assert.Equal("int unmatched-lateral-row", Typed(doc, "line_id"));
        Assert.Equal("int nullable-column unmatched-lateral-row", Typed(doc, "qty"));
        Assert.Equal(IrCardinality.Many, Result(doc).Cardinality);
    }

    [Fact]
    public void A_view_inside_a_function_is_read_from_its_definition()
    {
        var doc = Procedure("SELECT f.id, f.name FROM dbo.via_view(@id) AS f;");
        AllTyped(doc);
        Assert.Equal("nvarchar(10) nullable-column", Typed(doc, "name"));
        Assert.Equal(IrCardinality.AtMostOne, Result(doc).Cardinality);
    }

    [Fact]
    public void A_view_inside_a_scalar_functions_json_is_read_too()
    {
        var doc = Procedure("SELECT JSON_QUERY(dbo.names_json()) AS names;", "");
        Assert.IsType<IrArray>(Field(doc, "names").Type is IrJsonText { Content: { } content } ? content : Field(doc, "names").Type);
        Assert.DoesNotContain(doc.Unknowns, u => u.Reason == IrUnknownReason.UnresolvedTable);
    }

    [Fact]
    public void Nolock_inside_a_function_drops_its_key() => Assert.Equal(IrCardinality.Many, Result(Procedure("SELECT f.id FROM dbo.dirty(@id) AS f;")).Cardinality);

    [Fact]
    public void Read_uncommitted_in_the_procedure_drops_the_functions_bound() => Assert.Equal(IrCardinality.Many, Result(Procedure("SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED; SELECT f.id FROM dbo.t_by_id(@id) AS f;")).Cardinality);

    [Fact]
    public void Default_passes_the_parameters_default() =>
        // Default_passes_the_parameters_default.
        Assert.Equal("int", Typed(Procedure("SELECT f.x FROM dbo.with_default(DEFAULT) AS f;"), "x"));

    [Fact]
    public void An_unqualified_function_name_resolves_like_a_tables() => Assert.Equal("int", Typed(Procedure("SELECT f.id FROM t_by_id(@id) AS f;"), "id"));

    [Fact]
    public void What_is_not_modelled_is_unknown()
    {
        // A column list at the call is refused (317); a scalar function is not a table. A body that is a UNION is read.
        Assert.StartsWith("unknown: unsupported-source", Typed(Procedure("SELECT f.a FROM dbo.t_by_id(@id) AS f (a, b, c);"), "a"), StringComparison.Ordinal);
        Assert.Equal("int parameter", Typed(Procedure("SELECT f.a FROM dbo.both(@id) AS f;"), "a"));
        Assert.StartsWith("unknown: unresolved-table", Typed(Procedure("SELECT f.x FROM dbo.scalar(@id) AS f;"), "x"), StringComparison.Ordinal);
        Assert.StartsWith("unknown: unresolved-table", Typed(Procedure("SELECT f.x FROM dbo.missing(@id) AS f;"), "x"), StringComparison.Ordinal);
    }

    // ---- multi-statement functions ---------------------------------------------------------------------------------

    [Fact]
    public void A_multi_statement_function_is_its_declared_return_table_with_its_keys()
    {
        // A_multi_statement_functions_return_table_enforces_its_constraints: NOT NULL, PRIMARY KEY, UNIQUE and CHECK.
        var doc = Procedure("SELECT m.id, m.label, m.code, m.score FROM dbo.ms(1) AS m WHERE m.id = @id;");
        AllTyped(doc);
        Assert.Equal("int", Typed(doc, "id"));
        Assert.Equal("nvarchar(10) nullable-column", Typed(doc, "label"));
        Assert.Equal("varchar(3)", Typed(doc, "code"));
        Assert.Equal("int nullable-column", Typed(doc, "score"));
        Assert.Equal("5", ((IrScalar)Field(doc, "score").Type).Facets!.Max!.Value);
        Assert.Equal(IrCardinality.AtMostOne, Result(doc).Cardinality);

        Assert.Equal(IrCardinality.AtMostOne, Result(Procedure("SELECT m.id FROM dbo.ms(1) AS m WHERE m.code = 'abc';")).Cardinality);
        Assert.Equal(IrCardinality.Many, Result(Procedure("SELECT m.id FROM dbo.ms(1) AS m;")).Cardinality);
    }

    [Fact]
    public void A_multi_statement_function_that_calls_a_procedure_is_its_declared_return_table()
    {
        // SQL Server creates such a function and refuses the call when it runs (557, observed:
        // A_function_that_calls_a_procedure_is_created_and_every_call_of_it_fails), so no call returns a row. Its body's walk
        // stops at the call, which is not followed, so the table is as declared, which no call contradicts. (Walking it once
        // failed on the call, with no procedures to follow.)
        var doc = InferProcedure.Infer(
            "dbo.p",
            ("log.sql", "CREATE PROCEDURE dbo.log_it AS SELECT 1 AS one;"),
            ("f.sql", """
                CREATE FUNCTION dbo.logged() RETURNS @r TABLE (id int NOT NULL, note nvarchar(max) NULL) AS
                BEGIN
                    EXEC dbo.log_it;
                    INSERT @r (id, note) VALUES (1, N'[]');
                    RETURN;
                END
                """),
            ("p.sql", "CREATE PROCEDURE dbo.p AS SELECT l.id, l.note FROM dbo.logged() AS l;"));

        AllTyped(doc);
        Assert.Equal("int", Typed(doc, "id"));
        Assert.Equal("nvarchar(max) nullable-column", Typed(doc, "note"));
    }

    [Fact]
    public void A_function_of_a_name_two_schemas_have_names_both_in_name_order_whatever_order_they_are_defined_in()
    {
        // b.f is defined first in source order and a.f second; the caller's default schema decides which is read.
        var json = IrJsonWriter.Write(InferProcedure.Infer(
            "dbo.caller",
            ("a.sql", "CREATE FUNCTION b.f() RETURNS TABLE AS RETURN SELECT 1 AS x;"),
            ("z.sql", "CREATE FUNCTION a.f() RETURNS TABLE AS RETURN SELECT 2 AS x;"),
            ("caller.sql", "CREATE PROCEDURE dbo.caller AS SELECT r.x FROM f() AS r;")));

        Assert.Contains("f is not schema-qualified, and a.f and b.f have that name.", json, StringComparison.Ordinal);
    }
}
