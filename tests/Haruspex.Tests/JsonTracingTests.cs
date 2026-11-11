using Haruspex.Cli;
using Haruspex.Ir;

namespace Haruspex.Tests;

/// <summary>
/// JSON built in the project, traced to where it is read (slice 6l): through variables, table variables, temporary tables and
/// multi-statement functions, as a JSON literal, and under json_query, OPENJSON and JSON_VALUE. Each rule rests on an observation
/// in the differential tests' <c>JsonTracingObservationTests</c>, named where it matters.
/// </summary>
public class JsonTracingTests
{
    private const string Tables = """
        CREATE TABLE sales.[order] (order_id int NOT NULL PRIMARY KEY, total decimal(9,2) NOT NULL, notes nvarchar(max) NULL);
        GO
        CREATE TABLE sales.order_line (line_id int NOT NULL PRIMARY KEY, order_id int NOT NULL, sku varchar(8) NOT NULL, qty int NOT NULL,
            colour nvarchar(10) NULL);
        GO
        """;

    private static IrDocument Infer(string body, string parameters = "@id int, @flag bit, @raw nvarchar(max)", string more = "")
    {
        using var dir = new TempDirectory();
        dir.File("schema.sql", "CREATE SCHEMA sales;\nGO\n");
        dir.File("tables.sql", Tables);
        if (more.Length > 0)
            dir.File("more.sql", more);
        dir.File("p.sql", $"CREATE PROCEDURE sales.p {parameters} AS\n{body}");
        var project = SqlProject.Load(dir.Path);
        Assert.Empty(project.Diagnostics);
        return project.Infer(project.Select([ObjectPattern.Parse("sales.p")]).Procedures);
    }

    private const string Lines = "DECLARE @lines nvarchar(max) = (SELECT l.sku, l.qty FROM sales.order_line AS l WHERE l.order_id = @id FOR JSON PATH);\n";

    private static IReadOnlyList<IrField> Fields(IrDocument doc, IrType type) => type switch
    {
        IrArray { Items: var items } => Fields(doc, items),
        IrRef r => doc.Types[r.Name].Fields,
        var other => throw new InvalidOperationException($"Unexpected type {other}."),
    };

    private static IrField Field(IrDocument doc, string name, int result = 0) =>
        Assert.Single(Fields(doc, (doc.Objects[0].Results ?? throw new InvalidOperationException(string.Join("; ", doc.Unknowns.Select(u => u.Detail))))[result].Type), f => f.Name == name);

    private static IrField Sub(IrDocument doc, IrType type, string name) => Assert.Single(Fields(doc, type), f => f.Name == name);

    private static IrUnknown Unknown(IrDocument doc, string field) => Assert.Single(doc.Unknowns, u => u.Path.EndsWith("." + field, StringComparison.Ordinal));

    private static string[] Rules(IrField field) => [.. field.NullableBecause.Select(r => r.Rule)];

    /// <summary>The lines' item type: an object of sku and qty, each never null.</summary>
    private static void AssertLines(IrDocument doc, IrType type)
    {
        var items = Assert.IsType<IrArray>(type).Items;
        Assert.Equal(["sku", "qty"], Fields(doc, items).Select(f => f.Name));
        Assert.All(Fields(doc, items), f => Assert.False(f.Nullable));
    }

    // ---- variables ----------------------------------------------------------------------------------------------------

    [Fact]
    public void A_variable_holding_built_json_is_text_of_its_content_until_json_query_makes_it_live()
    {
        // Observed: Json_built_here_keeps_its_content_through_variables_and_tables_until_json_query.
        var doc = Infer(Lines + "SELECT @lines AS t, json_query(@lines) AS lines FOR JSON PATH;");

        AssertLines(doc, Assert.IsType<IrJsonText>(Field(doc, "t").Type).Content!);
        var lines = Field(doc, "lines");
        AssertLines(doc, lines.Type);
        Assert.Equal(1, Assert.IsType<IrArray>(lines.Type).MinItems);

        // FOR JSON over no rows is NULL, so the key is left out.
        Assert.Equal(["empty-subquery"], Rules(lines));
        Assert.Empty(doc.Unknowns);
    }

    [Fact]
    public void A_variable_assigned_by_a_select_over_rows_holds_the_json_or_its_value_before()
    {
        // Observed: an assignment by a SELECT that matches no rows does not happen.
        var doc = Infer(Lines + """
            DECLARE @summary nvarchar(max), @kept nvarchar(max) = json_object('total': 0, 'items': json_query('[]'));
            SELECT @summary = json_object('total': o.total, 'items': json_query(@lines)),
                   @kept = json_object('total': o.total, 'items': json_query(@lines))
            FROM sales.[order] AS o WHERE o.order_id = @id;
            SELECT json_query(@summary) AS summary, json_query(@kept) AS kept FOR JSON PATH;
            """);

        var summary = Field(doc, "summary");
        Assert.Equal(["variable"], Rules(summary));
        Assert.Equal(["total", "items"], Fields(doc, summary.Type).Select(f => f.Name));

        // @kept is its first value or the select's: never null, its items an empty tuple or the lines, so an array of lines.
        var kept = Field(doc, "kept");
        Assert.False(kept.Nullable);
        var items = Sub(doc, kept.Type, "items");
        AssertLines(doc, items.Type);
        Assert.Null(Assert.IsType<IrArray>(items.Type).MinItems);
        Assert.Empty(doc.Unknowns);
    }

    [Fact]
    public void Assignments_on_several_paths_join_their_json()
    {
        var doc = Infer("""
            DECLARE @x nvarchar(max);
            IF @flag = 1 SET @x = json_object('kind': 'a', 'n': 1);
            ELSE SET @x = json_object('kind': 'b', 'n': 2);
            SELECT json_query(@x) AS x FOR JSON PATH;
            """);

        var x = Field(doc, "x");
        Assert.False(x.Nullable);
        Assert.Equal(["kind", "n"], Fields(doc, x.Type).Select(f => f.Name));
        Assert.All(Fields(doc, x.Type), f => Assert.False(f.Nullable));
        Assert.Empty(doc.Unknowns);
    }

    [Theory]
    [InlineData("SET @x = @raw;", "@raw")]
    [InlineData("SET @x = json_modify(@x, '$.n', 2);", "json_modify")]
    [InlineData("SET @x = @x + N'';", "@x")]
    [InlineData("SELECT @x = o.notes FROM sales.[order] AS o WHERE o.order_id = @id;", "o.notes")]
    [InlineData("SET @x = '';", "not valid JSON")]
    public void An_assignment_that_may_not_be_json_built_here_makes_its_content_unknown_naming_it(string assignment, string named)
    {
        var doc = Infer($"""
            DECLARE @x nvarchar(max) = json_object('n': 1);
            IF @flag = 1 {assignment}
            SELECT json_query(@x) AS x FOR JSON PATH;
            """);

        var unknown = Unknown(doc, "x");
        Assert.Equal(IrUnknownReasons.JsonShapeUnknown, unknown.Reason);
        Assert.Contains(named, unknown.Detail, StringComparison.Ordinal);
        Assert.StartsWith("p.sql:3:", unknown.At, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("nvarchar(4000)")]
    [InlineData("varchar(max)")]
    public void A_variable_of_another_type_is_not_traced(string type)
    {
        // Observed: a shorter type cuts the text short without an error; varchar changes what its code page lacks.
        var doc = Infer($"DECLARE @x {type} = json_object('n': 1);\nSELECT json_query(@x) AS x FOR JSON PATH;");
        Assert.Contains("not built here", Unknown(doc, "x").Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_variable_in_a_loop_reaches_a_fixed_point_over_its_json()
    {
        var doc = Infer("""
            DECLARE @x nvarchar(max) = '[]', @n int = 0;
            WHILE @n < 3
            BEGIN
                SET @x = (SELECT l.sku FROM sales.order_line AS l WHERE l.order_id = @n FOR JSON PATH);
                SET @n += 1;
            END
            SELECT json_query(@x) AS x FOR JSON PATH;
            """);

        var x = Assert.IsType<IrArray>(Field(doc, "x").Type);
        Assert.Null(x.MinItems);
        Assert.Equal(["sku"], Fields(doc, x.Items).Select(f => f.Name));
    }

    // ---- literals -----------------------------------------------------------------------------------------------------

    [Fact]
    public void A_json_literal_is_its_exact_shape()
    {
        // Observed: A_json_literal_is_read_exactly_and_a_bare_scalar_or_text_that_is_not_json_is_an_error.
        var doc = Infer("""SELECT json_query('[]') AS empty, json_query(N' {"s": "x", "n": 1.50, "b": true, "z": null, "a": [1, "y"]} ') AS obj FOR JSON PATH;""");

        Assert.Empty(Assert.IsType<IrTuple>(Field(doc, "empty").Type).Items);
        var obj = Field(doc, "obj");
        Assert.False(obj.Nullable);
        Assert.Equal(["x"], Assert.IsType<IrScalar>(Sub(doc, obj.Type, "s").Type).Facets!.Values!.Items.Select(v => v.Text));
        Assert.Equal(["1.5"], Assert.IsType<IrScalar>(Sub(doc, obj.Type, "n").Type).Facets!.Values!.Items.Select(v => v.Text));
        Assert.Equal(IrScalarKind.Boolean, Assert.IsType<IrScalar>(Sub(doc, obj.Type, "b").Type).Kind);
        Assert.Equal(["null-literal"], Rules(Sub(doc, obj.Type, "z")));
        Assert.Equal(IrPresence.Always, Sub(doc, obj.Type, "z").Presence);
        Assert.Equal(2, Assert.IsType<IrTuple>(Sub(doc, obj.Type, "a").Type).Items.Count);
        Assert.Empty(doc.Unknowns);
    }

    [Theory]
    [InlineData("'1'", "bare JSON scalar")]
    [InlineData("'null'", "bare JSON scalar")]
    [InlineData("''", "not valid JSON")]
    [InlineData("N''", "not valid JSON")]
    [InlineData("'[1]x'", "not valid JSON")]
    [InlineData("'{\"a\":1,\"a\":2}'", "key written twice")]
    public void A_literal_that_is_not_a_json_document_is_unknown_saying_why(string literal, string why)
    {
        var doc = Infer($"SELECT json_query({literal}) AS x FOR JSON PATH;");
        Assert.Contains(why, Unknown(doc, "x").Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Coalesce_case_and_isnull_with_an_empty_array_keep_the_arrays_items()
    {
        // Observed: Under_json_query_coalesce_case_and_isnull_with_a_json_literal_give_the_json_of_the_branch_taken.
        var doc = Infer(Lines + """
            SELECT json_query(COALESCE(@lines, '[]')) AS a,
                   json_query(CASE WHEN @flag = 1 THEN (SELECT l.sku, l.qty FROM sales.order_line AS l FOR JSON PATH) ELSE '[]' END) AS b,
                   json_query(ISNULL(@lines, '[]')) AS c,
                   json_query(IIF(@flag = 1, @lines, N'[]')) AS d
            FOR JSON PATH;
            """);

        foreach (var name in new[] { "a", "b", "c", "d" })
        {
            var field = Field(doc, name);
            AssertLines(doc, field.Type);
            Assert.Null(Assert.IsType<IrArray>(field.Type).MinItems);
        }

        Assert.False(Field(doc, "a").Nullable);
        Assert.False(Field(doc, "c").Nullable);
        Assert.Equal(["empty-subquery"], Rules(Field(doc, "b")));
        Assert.Equal(["empty-subquery"], Rules(Field(doc, "d")));
        Assert.Empty(doc.Unknowns);
    }

    [Fact]
    public void Isnull_writes_any_json_literal_fallback_as_json()
    {
        var doc = Infer("""SELECT ISNULL((SELECT l.qty FROM sales.order_line AS l FOR JSON PATH), '{"none":true}') AS x FOR JSON PATH;""");
        var x = Assert.IsType<IrUnion>(Field(doc, "x").Type);
        Assert.Equal(2, x.Variants.Count);
        Assert.Empty(doc.Unknowns);
    }

    // ---- tables -------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_table_variable_or_temporary_table_column_holds_the_json_written_to_it()
    {
        var doc = Infer("""
            DECLARE @t TABLE (id int NOT NULL, lines nvarchar(max) NULL);
            CREATE TABLE #t (id int NOT NULL, lines nvarchar(max) NULL);
            INSERT @t (id, lines) SELECT o.order_id, (SELECT l.sku, l.qty FROM sales.order_line AS l WHERE l.order_id = o.order_id FOR JSON PATH) FROM sales.[order] AS o;
            INSERT #t (id, lines) VALUES (1, '[]');
            UPDATE #t SET lines = (SELECT l.sku, l.qty FROM sales.order_line AS l FOR JSON PATH);
            SELECT json_query(v.lines) AS v, json_query(t.lines) AS t FROM @t AS v CROSS JOIN #t AS t FOR JSON PATH;
            """);

        AssertLines(doc, Field(doc, "v").Type);
        AssertLines(doc, Field(doc, "t").Type);
        Assert.Equal(1, Assert.IsType<IrArray>(Field(doc, "v").Type).MinItems);
        Assert.Null(Assert.IsType<IrArray>(Field(doc, "t").Type).MinItems);
        Assert.Empty(doc.Unknowns);
    }

    [Theory]
    [InlineData("INSERT @t (id) VALUES (1);", "default")]
    [InlineData("UPDATE @t SET lines = @raw;", "@raw")]
    [InlineData("WITH c AS (SELECT t.id, t.lines FROM @t AS t) UPDATE c SET lines = N'x';", "CTE")]
    [InlineData("MERGE @t AS t USING sales.[order] AS o ON t.id = o.order_id WHEN NOT MATCHED THEN INSERT (id, lines) VALUES (o.order_id, o.notes);", "not read")]
    [InlineData("INSERT @t (id, lines) OUTPUT inserted.id, inserted.lines INTO @t (id, lines) VALUES (2, N'x');", "not valid JSON")]
    public void A_write_to_a_table_variable_whose_value_is_not_built_json_makes_it_unknown(string write, string named)
    {
        var doc = Infer($"""
            DECLARE @t TABLE (id int NOT NULL, lines nvarchar(max) NULL);
            INSERT @t (id, lines) VALUES (1, json_object('n': 1));
            {write}
            SELECT json_query(t.lines) AS x FROM @t AS t FOR JSON PATH;
            """);

        Assert.Contains(named, Unknown(doc, "x").Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_multi_statement_functions_return_table_holds_the_json_its_body_writes()
    {
        var doc = Infer("SELECT f.id, json_query(f.lines) AS lines FROM sales.lines_of(@id) AS f FOR JSON PATH;", more: """
            CREATE FUNCTION sales.lines_of(@id int) RETURNS @r TABLE (id int NOT NULL, lines nvarchar(max) NULL) AS
            BEGIN
                INSERT @r (id, lines) SELECT o.order_id, (SELECT l.sku, l.qty FROM sales.order_line AS l WHERE l.order_id = o.order_id FOR JSON PATH)
                FROM sales.[order] AS o WHERE o.order_id = @id;
                IF @@ROWCOUNT = 0 INSERT @r (id, lines) VALUES (0, '[]');
                RETURN;
            END
            GO
            """);

        AssertLines(doc, Field(doc, "lines").Type);
        Assert.Empty(doc.Unknowns);
    }

    // ---- OPENJSON and JSON_VALUE --------------------------------------------------------------------------------------

    [Fact]
    public void Openjson_with_over_traced_json_is_never_null_where_every_row_has_a_value_that_converts()
    {
        // Observed: Openjson_with_over_json_built_here_converts_its_own_types_exactly_and_cuts_strings_short.
        var doc = Infer("""
            DECLARE @lines nvarchar(max) = (SELECT l.line_id, l.sku, l.qty, l.colour FROM sales.order_line AS l WHERE l.order_id = @id FOR JSON PATH);
            SELECT j.line_id, j.sku, j.qty, j.big, j.short, j.small, j.colour, j.strict_colour, j.missing
            FROM OPENJSON(@lines) WITH (line_id int, sku varchar(8), qty int, big bigint '$.qty', short nvarchar(3) '$.sku', small tinyint '$.qty',
                                         colour nvarchar(10), strict_colour nvarchar(10) 'strict $.colour', missing int) AS j;
            """);

        // Back into its own type: never null, with its facts.
        Assert.False(Field(doc, "line_id").Nullable);
        Assert.False(Field(doc, "sku").Nullable);
        Assert.False(Field(doc, "qty").Nullable);
        Assert.False(Field(doc, "big").Nullable);

        // A shorter string is cut short, never null.
        Assert.False(Field(doc, "short").Nullable);

        // A narrower number may overflow; a key FOR JSON leaves out when null is NULL, and under strict is an error, but then
        // the call fails; a key never there is NULL.
        Assert.Equal(["json-value"], Rules(Field(doc, "small")));
        Assert.Equal(["json-value"], Rules(Field(doc, "colour")));
        Assert.False(Field(doc, "strict_colour").Nullable);
        Assert.Equal(["json-value"], Rules(Field(doc, "missing")));
        Assert.Empty(doc.Unknowns);
    }

    [Fact]
    public void Openjson_with_carries_a_literals_values_and_as_json_keeps_the_content()
    {
        var doc = Infer("""
            DECLARE @x nvarchar(max) = N'{"kind": "a", "tags": ["x", "y"]}';
            SELECT j.kind, j.tags FROM OPENJSON(@x) WITH (kind nvarchar(5), tags nvarchar(max) AS JSON) AS j FOR JSON PATH;
            """);

        var kind = Field(doc, "kind");
        Assert.False(kind.Nullable);
        Assert.Equal(["a"], Assert.IsType<IrScalar>(kind.Type).Facets!.Values!.Items.Select(v => v.Text));
        Assert.Equal(2, Assert.IsType<IrTuple>(Field(doc, "tags").Type).Items.Count);
        Assert.Empty(doc.Unknowns);
    }

    [Fact]
    public void Openjson_without_with_over_traced_json_has_the_keys_and_types_the_shape_fixes()
    {
        // Observed: Openjson_without_with_over_json_built_here_gives_its_keys_and_their_types.
        var doc = Infer("""
            DECLARE @x nvarchar(max) = json_object('n': 1, 's': N'x', 'b': CAST(1 AS bit), 'z': NULL);
            SELECT j.[key], j.value, j.type FROM OPENJSON(@x) AS j;
            """);

        Assert.Equal(["b", "n", "s", "z"], Assert.IsType<IrScalar>(Field(doc, "key").Type).Facets!.Values!.Items.Select(v => v.Text));
        Assert.Equal(["0", "1", "2", "3"], Assert.IsType<IrScalar>(Field(doc, "type").Type).Facets!.Values!.Items.Select(v => v.Text));
        Assert.Equal(["json-value"], Rules(Field(doc, "value")));
    }

    [Fact]
    public void Json_value_over_traced_json_is_never_null_where_the_path_finds_a_short_scalar()
    {
        // Observed: Json_value_over_json_built_here_gives_its_scalars_as_text.
        var doc = Infer("""
            DECLARE @x nvarchar(max) = (SELECT TOP (1) l.sku, l.colour, CAST(1 AS bit) AS b FROM sales.order_line AS l FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
            DECLARE @y nvarchar(max) = N'{"sku": "ab12", "flag": false}';
            SELECT json_value(@x, '$.sku') AS a, json_value(@x, '$.colour') AS b, json_value(@y, '$.sku') AS c, json_value(@y, 'strict $.flag') AS d;
            """);

        // @x may be NULL (no rows), so its values may be, and colour may be left out; @y is never NULL.
        Assert.Equal(["empty-subquery"], Rules(Field(doc, "a")));
        Assert.Equal(["empty-subquery", "json-value"], Rules(Field(doc, "b")));
        var c = Field(doc, "c");
        Assert.False(c.Nullable);
        Assert.Equal(["ab12"], Assert.IsType<IrScalar>(c.Type).Facets!.Values!.Items.Select(v => v.Text));
        var d = Field(doc, "d");
        Assert.False(d.Nullable);
        Assert.Equal(["false", "true"], Assert.IsType<IrScalar>(d.Type).Facets!.Values!.Items.Select(v => v.Text));
    }
}
