using Haruspex.Collections;
using Haruspex.Ir;

namespace Haruspex.Tests.Inference;

/// <summary>
/// Expressions whose type depends on what SQL Server folds to a constant: an int's digits beside a decimal, and whether
/// <c>COALESCE</c>, a <c>CASE</c>, <c>NULLIF</c> or a <c>UNION</c> over JSON nests as JSON or as text. Each rule rests on an
/// observation in the differential tests' <c>FoldingObservationTests</c>, made at every compatibility level from 130 to 170,
/// named where it matters (Haruspex.SqlServer.Inference's README.md, "What the server folds"; decision 0088).
/// </summary>
public class FoldingTests
{
    private const string Tables = """
        CREATE TABLE dbo.sale (
            id int NOT NULL PRIMARY KEY, qty int NOT NULL, price decimal(9,2) NOT NULL, frac decimal(38,38) NOT NULL,
            code varchar(10) NOT NULL);
        GO
        CREATE TABLE dbo.t (id int NOT NULL PRIMARY KEY, x int NULL, short varchar(10) NULL);
        GO
        CREATE TABLE dbo.u (id int NOT NULL PRIMARY KEY, t_id int NOT NULL, qty int NOT NULL);
        GO
        """;

    private const string Lines = "(SELECT u.qty FROM dbo.u AS u WHERE u.t_id = t.id FOR JSON PATH)";

    private static IrDocument Procedure(string body, string parameters = "") =>
        InferProcedure.Infer("dbo.p", ("tables.sql", Tables), ("p.sql", $"CREATE PROCEDURE dbo.p {parameters} AS\n{body}"));

    private static EquatableArray<IrField> Fields(IrDocument doc, IrType type) => doc.Types[Assert.IsType<IrRef>(type).Name].Fields;

    /// <summary>The field <paramref name="name"/> of the first result's row.</summary>
    private static IrField Field(IrDocument doc, string name)
    {
        var results = doc.Objects[0].Results ?? throw new InvalidOperationException("The results are unknown: " + string.Join("; ", doc.Unknowns.Select(u => u.Detail)));
        return Assert.Single(Fields(doc, ((IrArray)results[0].Type).Items), f => f.Name == name);
    }

    private static IrNullRule[] Rules(IrDocument doc, string name) => [.. Field(doc, name).NullableBecause.Select(r => r.Rule)];

    private static bool IsUnknown(IrDocument doc, string name) => Field(doc, name).Type is IrUnknownType;

    /// <summary>The type and the reasons to be null of the field <c>v</c>, as one string, or why it is unknown.</summary>
    private static string Typed(IrDocument doc)
    {
        var field = Field(doc, "v");
        if (field.Type is not IrScalar { Sql: { } sql })
            return "unknown: " + string.Join("; ", doc.Unknowns.Select(u => u.Detail));
        return string.Join(" ", [sql, .. field.NullableBecause.Select(r => IrWireNames.Of(r.Rule))]);
    }

    // ---- an int's digits -------------------------------------------------------------------------------------------------

    [Fact]
    public void A_case_like_expression_reading_a_column_first_is_never_folded_and_counts_its_types_digits()
    {
        // Only_expressions_over_literals_are_folded_when_a_type_is_derived and
        // A_condition_on_a_column_is_never_folded_but_one_with_a_constant_or_null_may_be: decimal(9,2) divided by an int of 10
        // digits is decimal(20,13), of 1 or 2 decimal(13,6). Beside decimal(38,38) in a UNION, 10 digits give decimal(38,28).
        static string Divided(string divisor) => Typed(Procedure($"SELECT s.price / {divisor} AS v FROM dbo.sale AS s CROSS JOIN (SELECT 21 AS days) AS w;"));
        foreach (var divisor in (string[])[
            "CASE WHEN s.id > 0 THEN 5 ELSE 70 END", "IIF(s.qty IN (1, 2) AND s.code LIKE 'a%', 5, 70)", "CASE s.id WHEN 1 THEN 5 ELSE 70 END",
            "IIF(NOT (s.id BETWEEN 1 AND 2), 5, 70)", "IIF(w.days = 21, 5, 70)", "COALESCE(s.qty, 5)", "NULLIF(w.days, 0)", "ISNULL(5, 70)", "ISNULL(w.days, 1)",
        ])
        {
            Assert.StartsWith("decimal(20,13)", Divided(divisor), StringComparison.Ordinal);
        }

        Assert.Equal("decimal(38,28) precision-loss", Typed(Procedure("SELECT s.frac AS v FROM dbo.sale AS s UNION ALL SELECT IIF(s.id > 0, 1, 2) FROM dbo.sale AS s;")));
    }

    [Fact]
    public void A_derived_tables_literal_counts_its_types_digits_inside_an_expression()
    {
        // A_derived_tables_literal_counts_its_own_digits_only_as_a_set_operations_query: as the query of a UNION it counts its
        // own (above), but divided into a decimal, in parentheses, through a CTE or a VALUES list, it counts 10, so
        // decimal(9,2) / it is decimal(20,13), not decimal(13,6).
        Assert.StartsWith("decimal(20,13)", Typed(Procedure("SELECT s.price / w.days AS v FROM dbo.sale AS s CROSS JOIN (SELECT 21 AS days) AS w;")), StringComparison.Ordinal);
        Assert.StartsWith("decimal(20,13)", Typed(Procedure("SELECT s.price / (w.days) AS v FROM dbo.sale AS s CROSS JOIN (SELECT 21 AS days) AS w;")), StringComparison.Ordinal);
        Assert.StartsWith("decimal(20,13)", Typed(Procedure("WITH w AS (SELECT 21 AS days) SELECT s.price / w.days AS v FROM dbo.sale AS s CROSS JOIN w;")), StringComparison.Ordinal);
        Assert.StartsWith("decimal(20,13)", Typed(Procedure("SELECT s.price / r.days AS v FROM dbo.sale AS s CROSS JOIN (VALUES (21)) AS r(days);")), StringComparison.Ordinal);
        Assert.Equal("decimal(38,37) precision-loss", Typed(Procedure("SELECT s.frac AS v FROM dbo.sale AS s UNION ALL SELECT (w.one) FROM (SELECT 1 AS one) AS w;")));
        Assert.Equal("decimal(38,28) precision-loss", Typed(Procedure("SELECT s.frac AS v FROM dbo.sale AS s UNION ALL SELECT ISNULL(w.one, 2) FROM (SELECT 1 AS one) AS w;")));
    }

    [Theory]
    [InlineData("s.frac + -70000", "decimal(38,32)")]
    [InlineData("s.frac - -70000", "decimal(38,32)")]
    [InlineData("s.frac + (-70000)", "decimal(38,32)")]
    [InlineData("s.frac + NULLIF(-70000, 0)", "decimal(38,32)")]
    [InlineData("s.frac + NULLIF(NULLIF(-70000, 0), 0)", "decimal(38,32)")]
    [InlineData("s.price / NULLIF(-70000, 0)", "decimal(16,9)")]
    [InlineData("s.frac + -9999", "decimal(38,34)")]
    [InlineData("s.frac + -100000", "decimal(38,32)")]
    // A_negative_five_digit_literal_counts_six_digits_at_every_level_alone_or_folded_from_nullif: price is decimal(9,2), so
    // divided by six digits its scale is 2 + 6 + 1.
    public void A_negative_five_digit_literal_counts_six_digits_alone_or_folded_from_nullif(string expression, string type) =>
        Assert.StartsWith(type + " ", Typed(Procedure($"SELECT {expression} AS v FROM dbo.sale AS s;")) + " ", StringComparison.Ordinal);

    [Fact]
    public void Nullif_of_two_literals_counts_the_digits_it_folds_to_inside_an_expression_and_its_types_as_a_query()
    {
        // Nullif_of_two_literals_is_folded_inside_arithmetic_but_not_beside_other_values.
        Assert.StartsWith("decimal(38,37)", Typed(Procedure("SELECT s.frac + NULLIF(1, 0) AS v FROM dbo.sale AS s;")), StringComparison.Ordinal);
        Assert.StartsWith("decimal(38,37)", Typed(Procedure("SELECT s.frac + NULLIF(5, 5.0) AS v FROM dbo.sale AS s;")), StringComparison.Ordinal);
        Assert.StartsWith("decimal(38,33)", Typed(Procedure("SELECT s.frac + NULLIF(70000, 0) AS v FROM dbo.sale AS s;")), StringComparison.Ordinal);
        Assert.StartsWith("decimal(38,37)", Typed(Procedure("SELECT s.frac + NULLIF(1, '0') AS v FROM dbo.sale AS s;")), StringComparison.Ordinal);
        Assert.StartsWith("decimal(38,35)", Typed(Procedure("SELECT s.frac + NULLIF(1, s.id) AS v FROM dbo.sale AS s;")), StringComparison.Ordinal);
        Assert.StartsWith("decimal(38,35)", Typed(Procedure("SELECT s.frac AS v FROM dbo.sale AS s UNION ALL SELECT NULLIF(1, 0) FROM dbo.sale AS s;")), StringComparison.Ordinal);
        Assert.StartsWith("decimal(38,35)", Typed(Procedure("SELECT COALESCE(s.frac, NULLIF(1, 0)) AS v FROM dbo.sale AS s;")), StringComparison.Ordinal);

        // Nested, it folds however deep; a NULLIF of a literal and a constant it can't work out here is unknown.
        Assert.StartsWith("decimal(38,37)", Typed(Procedure("SELECT s.frac + NULLIF(NULLIF(1, 0), 0) AS v FROM dbo.sale AS s;")), StringComparison.Ordinal);
        Assert.StartsWith("decimal(38,33)", Typed(Procedure("SELECT s.frac + NULLIF(NULLIF(70000, 0), 0) AS v FROM dbo.sale AS s;")), StringComparison.Ordinal);
        Assert.StartsWith("decimal(38,37)", Typed(Procedure("SELECT s.frac + NULLIF(1, NULLIF(0, 1)) AS v FROM dbo.sale AS s;")), StringComparison.Ordinal);
        Assert.StartsWith("decimal(38,37)", Typed(Procedure("SELECT s.frac + NULLIF(NULLIF(21, 0), 21) AS v FROM dbo.sale AS s;")), StringComparison.Ordinal);
        Assert.StartsWith("decimal(38,35)", Typed(Procedure("SELECT s.frac + NULLIF(NULLIF(NULLIF(123, 0), 0), 0) AS v FROM dbo.sale AS s;")), StringComparison.Ordinal);
        Assert.StartsWith("unknown: ", Typed(Procedure("SELECT s.frac + NULLIF(1, IIF(1 = 1, 0, 1)) AS v FROM dbo.sale AS s;")), StringComparison.Ordinal);
        Assert.StartsWith("decimal(38,35)", Typed(Procedure("SELECT IIF(s.id > 0, s.frac, NULLIF(1, 0)) AS v FROM dbo.sale AS s;")), StringComparison.Ordinal);
    }

    // ---- JSON ------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Case_and_iif_over_json_on_a_tables_column_with_a_last_result_of_null_or_a_literal_are_text()
    {
        // A_case_over_json_on_a_tables_column_is_text_where_its_last_result_is_null_or_a_literal: never folded to one result, and
        // text in FOR JSON and json_object alike. JSON text of what the results hold, the literal parsed; NULL where no WHEN holds.
        var doc = Procedure($$"""
            SELECT t.id, CASE WHEN t.id = 1 THEN {{Lines}} ELSE '[]' END AS a, IIF(t.x IS NOT NULL, {{Lines}}, NULL) AS b,
                   CASE WHEN t.x IS NOT NULL THEN JSON_QUERY(JSON_OBJECT('system': 'x', 'id': t.x)) END AS c,
                   CASE WHEN t.x = 2 THEN JSON_OBJECT('id': t.x) ELSE JSON_QUERY(N'{"id":0}') END AS d
            FROM dbo.t AS t FOR JSON PATH;
            """);
        var a = Assert.IsType<IrArray>(Assert.IsType<IrJsonText>(Field(doc, "a").Type).Content);
        Assert.Null(a.MinItems);
        Assert.IsType<IrArray>(Assert.IsType<IrJsonText>(Field(doc, "b").Type).Content);
        Assert.Equal([IrNullRule.EmptySubquery, IrNullRule.NullLiteral], Rules(doc, "b"));
        Assert.Equal(["system", "id"], Fields(doc, Assert.IsType<IrJsonText>(Field(doc, "c").Type).Content!).Select(f => f.Name));
        Assert.Equal([IrNullRule.NoBranchMatched], Rules(doc, "c"));
        Assert.Equal(["id"], Fields(doc, Assert.IsType<IrJsonText>(Field(doc, "d").Type).Content!).Select(f => f.Name));
    }

    [Fact]
    public void Case_iif_choose_and_nullif_over_json_that_may_nest_as_json_are_not_typed()
    {
        // In json_object a CASE whose last result is JSON built here nests as JSON, a condition on a parameter is folded under
        // OPTION (RECOMPILE), and OR 1 = 1 is folded (A_case_over_json_on_a_tables_column_is_text_where_its_last_result_is_null_or_a_literal);
        // CHOOSE is not observed; NULLIF over live JSON nests as JSON in json_object only (Nullif_over_json_text_is_text_or_null_in_every_context).
        var doc = Procedure($"""
            SELECT t.id, CASE WHEN t.x IS NULL THEN NULL ELSE JSON_OBJECT('id': t.x) END AS a, IIF(@p = 1, {Lines}, '[]') AS b,
                   CHOOSE(t.id, {Lines}, '[]') AS c, NULLIF({Lines}, '[]') AS d, CASE WHEN t.x = 1 OR 1 = 1 THEN {Lines} END AS e
            FROM dbo.t AS t FOR JSON PATH;
            """, "@p int");
        foreach (var name in new[] { "a", "b", "c", "d", "e" })
            Assert.True(IsUnknown(doc, name), name);
        Assert.Contains(doc.Unknowns, u => u.Detail.Contains("json_object (observed), which one shape can't say", StringComparison.Ordinal));
    }

    [Fact]
    public void Json_shorter_than_max_through_a_case_on_a_column_is_not_typed()
    {
        // Json_of_a_type_shorter_than_max_stays_json_through_case_and_coalesce: json_query of a literal (nvarchar(4000)) or of a
        // cast to nvarchar(200) nests as JSON through the CASE, so the CASE is typed only beside a result of nvarchar(max) JSON.
        var doc = Procedure("""
            SELECT t.id, CASE WHEN t.x IS NOT NULL THEN JSON_QUERY(N'{"a":1}') END AS a,
                   IIF(t.x IS NOT NULL, JSON_QUERY(CAST(JSON_OBJECT('id': t.x) AS nvarchar(200))), NULL) AS b,
                   COALESCE(JSON_QUERY(CAST((SELECT JSON_ARRAYAGG(u.qty) FROM dbo.u AS u WHERE u.t_id = t.id) AS nvarchar(200))), N'[]') AS c
            FROM dbo.t AS t FOR JSON PATH;
            """);
        foreach (var name in new[] { "a", "b", "c" })
            Assert.True(IsUnknown(doc, name), name);
    }

    [Fact]
    public void Coalesce_whose_first_argument_is_json_built_from_rows_is_text_whatever_follows()
    {
        // Coalesce_over_json_built_from_rows_is_text_wherever_it_nests_at_every_level: an aggregate's subquery, converted or not,
        // json_query of a table's column and json_object are never folded, so COALESCE is text even beside a live fallback.
        var doc = Procedure("""
            SELECT t.id,
                   COALESCE((SELECT CONVERT(nvarchar(max), JSON_ARRAYAGG(u.qty ORDER BY u.id)) FROM dbo.u AS u WHERE u.t_id = t.id), N'[]') AS a,
                   COALESCE((SELECT JSON_ARRAYAGG(u.qty) FROM dbo.u AS u WHERE u.t_id = t.id), JSON_QUERY(N'[]')) AS b,
                   COALESCE(JSON_QUERY(t.short), N'[]') AS c, COALESCE(JSON_OBJECT('a': t.id), '{}') AS d
            FROM dbo.t AS t FOR JSON PATH;
            """);
        Assert.Equal("int", Assert.IsType<IrScalar>(Assert.IsType<IrArray>(Assert.IsType<IrJsonText>(Field(doc, "a").Type).Content).Items).Sql);
        Assert.Equal([IrNullRule.ReadTwice], Rules(doc, "a"));
        Assert.IsType<IrArray>(Assert.IsType<IrJsonText>(Field(doc, "b").Type).Content);
        Assert.IsType<IrJsonText>(Field(doc, "d").Type);

        // json_query of a stored column nests as text, but what the column holds is not known here (K1).
        Assert.True(IsUnknown(doc, "c"));
        Assert.Contains(doc.Unknowns, u => u.Detail.Contains("reads JSON that is not built here", StringComparison.Ordinal));
    }

    [Fact]
    public void Coalesce_over_json_the_server_may_fold_to_its_first_argument_is_not_typed()
    {
        // Coalesce_over_json_built_from_rows_is_text_wherever_it_nests_at_every_level: json_query of a constant is folded, even
        // through a derived table or a subquery over a table; json_query of a variable may be, under OPTION (RECOMPILE).
        var doc = Procedure("""
            DECLARE @v nvarchar(max) = N'[1]';
            SELECT t.id, COALESCE(w.j, N'[]') AS a, COALESCE((SELECT TOP (1) JSON_QUERY(N'[1]') FROM dbo.u AS u), N'[]') AS b,
                   COALESCE(JSON_QUERY(@v), N'[]') AS c
            FROM dbo.t AS t CROSS JOIN (SELECT JSON_QUERY(N'[1]') AS j) AS w FOR JSON PATH;
            """);
        foreach (var name in new[] { "a", "b", "c" })
            Assert.True(IsUnknown(doc, name), name);
    }

    [Fact]
    public void Nullif_over_json_text_is_that_text_or_null()
    {
        // Nullif_over_json_text_is_text_or_null_in_every_context.
        var doc = Procedure("""
            SELECT t.id, NULLIF((SELECT TOP (1) u.qty AS n FROM dbo.u AS u WHERE u.t_id = t.id ORDER BY u.id FOR JSON PATH, WITHOUT_ARRAY_WRAPPER), N'') AS due
            FROM dbo.t AS t FOR JSON PATH;
            """);
        Assert.Equal(["n"], Fields(doc, Assert.IsType<IrJsonText>(Field(doc, "due").Type).Content!).Select(f => f.Name));
        Assert.Contains(IrNullRule.NullWhenEqual, Rules(doc, "due"));
    }

    [Fact]
    public void A_union_over_json_built_from_rows_is_json_text_of_what_its_queries_hold()
    {
        // A_union_over_json_built_from_rows_is_text_in_every_row: text in every row, its content joined across the queries.
        var doc = Procedure("SELECT (SELECT u.id FROM dbo.u AS u FOR JSON PATH) AS j UNION ALL SELECT (SELECT u.id FROM dbo.u AS u WHERE u.qty > 1 FOR JSON PATH);");
        Assert.IsType<IrArray>(Assert.IsType<IrJsonText>(Field(doc, "j").Type).Content);
        doc = Procedure("SELECT JSON_QUERY((SELECT u.id FROM dbo.u AS u FOR JSON PATH)) AS j UNION SELECT JSON_QUERY(N'[]');");
        Assert.IsType<IrArray>(Assert.IsType<IrJsonText>(Field(doc, "j").Type).Content);

        // Text holding JSON stays text whose content is the same.
        doc = Procedure("SELECT (SELECT u.id FROM dbo.u AS u FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) AS j UNION ALL SELECT (SELECT u.id FROM dbo.u AS u FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);");
        Assert.IsType<IrJsonText>(Field(doc, "j").Type);
    }

    [Fact]
    public void A_set_operation_over_json_that_may_come_out_as_json_is_not_modelled()
    {
        // A_union_over_json_built_from_rows_is_text_in_every_row: json_query of constants comes out as JSON, the text beside it
        // written in as it is; EXCEPT and INTERSECT keep the first query's form.
        foreach (var sql in (string[])[
            "SELECT JSON_QUERY(N'[1]') AS j UNION ALL SELECT JSON_QUERY(N'[2]');",
            "SELECT JSON_QUERY(N'[1]') AS j UNION ALL SELECT N'plain';",
            "SELECT (SELECT u.id FROM dbo.u AS u FOR JSON PATH) AS j EXCEPT SELECT JSON_QUERY(N'[]');",
            "SELECT (SELECT u.id FROM dbo.u AS u FOR JSON PATH) AS j INTERSECT SELECT (SELECT u.qty FROM dbo.u AS u FOR JSON PATH);",
            "SELECT (SELECT u.id FROM dbo.u AS u FOR JSON PATH) AS j UNION ALL SELECT N'plain';",
        ])
        {
            Assert.IsType<IrUnknownType>(Field(Procedure(sql), "j").Type);
        }
    }
}
