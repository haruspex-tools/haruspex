using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

public partial class JsonTracingTests
{
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
        Assert.Equal([IrNullRule.JsonValue], Rules(Field(doc, "small")));
        Assert.Equal([IrNullRule.JsonValue], Rules(Field(doc, "colour")));
        Assert.False(Field(doc, "strict_colour").Nullable);
        Assert.Equal([IrNullRule.JsonValue], Rules(Field(doc, "missing")));
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
    public void An_nchar_openjson_column_is_at_most_its_length_unless_it_reads_its_own_type()
    {
        // Observed: Openjson_with_does_not_pad_nchar_but_pads_char.
        var doc = Infer("""
            DECLARE @x nvarchar(max) = (SELECT CAST(l.sku AS nchar(8)) AS fixed, l.colour FROM sales.order_line AS l FOR JSON PATH);
            SELECT j.fixed, j.colour, d.v FROM OPENJSON(@x) WITH (fixed nchar(8), colour nchar(10)) AS j
            CROSS APPLY OPENJSON(N'[{"v":"ab"}]') WITH (v nchar(4)) AS d;
            """);

        Assert.True(Assert.IsType<IrScalar>(Field(doc, "fixed").Type).Facets!.Length!.IsFixed);
        Assert.False(Assert.IsType<IrScalar>(Field(doc, "colour").Type).Facets!.Length!.IsFixed);
        Assert.False(Assert.IsType<IrScalar>(Field(doc, "v").Type).Facets!.Length!.IsFixed);
    }

    [Fact]
    public void A_varchar_read_back_into_itself_keeps_its_set_only_when_every_value_is_ascii()
    {
        // Observed: Openjson_with_converts_a_varchar_to_the_databases_code_page.
        var doc = Infer("""
            DECLARE @x nvarchar(max) = (SELECT g.greek, g.plain FROM sales.glyph AS g FOR JSON PATH);
            SELECT g.greek, g.plain FROM sales.glyph AS g;
            SELECT j.greek, j.plain FROM OPENJSON(@x) WITH (greek varchar(10), plain varchar(10)) AS j;
            """, more: """
            CREATE TABLE sales.glyph (id int NOT NULL PRIMARY KEY,
                greek varchar(10) COLLATE Greek_CI_AS NOT NULL CHECK (greek IN (N'α', N'β')),
                plain varchar(10) NOT NULL CHECK (plain IN ('a', 'b')));
            GO
            """);

        // The table's own column has its set; read back through JSON only the ASCII one keeps it.
        Assert.NotNull(Assert.IsType<IrScalar>(Field(doc, "greek").Type).Facets!.Values);
        Assert.Null(Assert.IsType<IrScalar>(Field(doc, "greek", 1).Type).Facets?.Values);
        Assert.NotNull(Assert.IsType<IrScalar>(Field(doc, "plain", 1).Type).Facets!.Values);
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
        Assert.Equal([IrNullRule.JsonValue], Rules(Field(doc, "value")));
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
        Assert.Equal([IrNullRule.EmptySubquery], Rules(Field(doc, "a")));
        Assert.Equal([IrNullRule.EmptySubquery, IrNullRule.JsonValue], Rules(Field(doc, "b")));
        var c = Field(doc, "c");
        Assert.False(c.Nullable);
        Assert.Equal(["ab12"], Assert.IsType<IrScalar>(c.Type).Facets!.Values!.Items.Select(v => v.Text));
        var d = Field(doc, "d");
        Assert.False(d.Nullable);
        Assert.Equal(["false", "true"], Assert.IsType<IrScalar>(d.Type).Facets!.Values!.Items.Select(v => v.Text));
    }
}
