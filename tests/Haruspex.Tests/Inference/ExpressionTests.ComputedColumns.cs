using Haruspex.Ir;

namespace Haruspex.Tests.Inference;

public partial class ExpressionTests
{
    /// <summary>A table of computed columns, PERSISTED or not, over a never-null int, a nullable int and a string.</summary>
    private const string ComputedTables = """
        CREATE TABLE dbo.gauge (
            id int NOT NULL PRIMARY KEY, a int NOT NULL, b int NULL, s varchar(10) NOT NULL, code varchar(12) NULL,
            is_high AS (CASE WHEN a > 5 THEN 1 ELSE 0 END), small AS (CAST(a AS tinyint)) PERSISTED, doubled AS (a * 2),
            next_b AS (b + 1), part AS (PARSENAME(REPLACE(code, '-', '.'), 1)), label AS (CONCAT(s, 'x')) PERSISTED NOT NULL,
            kept AS (b * 2) PERSISTED NOT NULL, blob AS (CAST(s AS varbinary(10))), doc AS (JSON_OBJECT('a': a)));
        CREATE TABLE dbo.reading (id int NOT NULL PRIMARY KEY, gauge_id int NULL);
        GO
        """;

    private static IrDocument Computed(string body) =>
        InferProcedure.Infer("dbo.p", ("tables.sql", ComputedTables), ("p.sql", $"CREATE PROCEDURE dbo.p AS\n{body}"));

    [Fact]
    public void A_computed_column_is_typed_as_its_expression_persisted_or_not()
    {
        // A_computed_column_is_of_its_expressions_type_and_null_as_its_expression_is_persisted_or_not. Each takes its expression's
        // type and its reasons to be null, read over the same row; PERSISTED NOT NULL is never null, since a row whose value
        // would be NULL is refused when it is written.
        var doc = Computed("SELECT g.is_high, g.small, g.doubled, g.next_b, g.part, g.label, g.kept FROM dbo.gauge AS g;");
        Assert.Equal("int", Typed(doc, "is_high"));
        Assert.Equal("tinyint arithmetic-overflow", Typed(doc, "small"));
        Assert.Equal("int arithmetic-overflow", Typed(doc, "doubled"));
        Assert.Equal("int nullable-column arithmetic-overflow", Typed(doc, "next_b"));
        Assert.Equal("nvarchar(128) nullable-column out-of-range", Typed(doc, "part"));
        Assert.Equal("varchar(11)", Typed(doc, "label"));
        Assert.Equal("int", Typed(doc, "kept"));
    }

    [Fact]
    public void A_computed_column_keeps_its_expressions_value_set_and_takes_the_reasons_of_where_it_is_read()
    {
        // The CASE gives 0 or 1; read from the optional side of a join it may also be NULL there.
        var doc = Computed("SELECT r.id, g.is_high FROM dbo.reading AS r LEFT JOIN dbo.gauge AS g ON g.id = r.gauge_id;");
        Assert.Equal("int unmatched-row", Typed(doc, "is_high"));
        Assert.Equal(["0", "1"], Assert.IsType<IrScalar>(Field(doc, "is_high").Type).Facets?.Values?.Items.Select(v => v.Text));

        // Grouped by, it is read as a grouped column: the columns its expression reads need not be grouped.
        var grouped = Computed("SELECT g.is_high, COUNT(*) AS n FROM dbo.gauge AS g GROUP BY g.is_high;");
        Assert.Empty(grouped.Unknowns);
        Assert.Equal("int", Typed(grouped, "is_high"));
    }

    [Fact]
    public void A_computed_column_whose_expression_is_unknown_is_unknown_with_its_reason()
    {
        // A conversion to binary isn't modelled, so neither is a column computed as one: its reason is the expression's, where the
        // table's file writes it. A column computed as JSON nests as JSON in FOR
        // JSON (A_computed_column_of_json_nests_as_json_in_for_json), which is not modelled for a column.
        var doc = Computed("SELECT g.blob, g.doc FROM dbo.gauge AS g;");
        Assert.Contains(doc.Unknowns, u => u.Path.EndsWith(".blob", StringComparison.Ordinal) && u.Reason == IrUnknownReason.UnsupportedExpression
            && u.Detail.StartsWith("CAST(s AS varbinary(10))", StringComparison.Ordinal) && u.At.StartsWith("tables.sql:", StringComparison.Ordinal));
        Assert.Contains(doc.Unknowns, u => u.Path.EndsWith(".doc", StringComparison.Ordinal) && u.Detail.Contains("JSON", StringComparison.Ordinal));
    }

    [Fact]
    public void A_computed_column_read_by_another_is_unknown_as_sql_server_refuses_it()
    {
        // A_persisted_computed_column_is_written_only_with_ansi_warnings_on_and_a_computed_column_reads_no_other: 1759.
        var doc = InferProcedure.Infer("dbo.p",
            ("tables.sql", "CREATE TABLE dbo.chain (a int NOT NULL, b AS (c + 1), c AS (b + 1));\nGO\n"),
            ("p.sql", "CREATE PROCEDURE dbo.p AS\nSELECT b FROM dbo.chain;"));
        Assert.Contains(doc.Unknowns, u => u.Detail.Contains("another computed column", StringComparison.Ordinal));
    }
}
