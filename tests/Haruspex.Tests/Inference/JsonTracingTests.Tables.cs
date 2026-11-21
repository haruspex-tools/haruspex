using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

public partial class JsonTracingTests
{
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

    [Fact]
    public void A_table_variable_column_holds_the_json_a_merge_writes_to_it()
    {
        var doc = Infer("""
            DECLARE @t TABLE (id int NOT NULL, lines nvarchar(max) NULL);
            MERGE @t AS t USING sales.[order] AS o ON t.id = o.order_id
            WHEN MATCHED THEN UPDATE SET lines = (SELECT l.sku, l.qty FROM sales.order_line AS l WHERE l.order_id = o.order_id FOR JSON PATH)
            WHEN NOT MATCHED THEN INSERT (id, lines) VALUES (o.order_id, (SELECT l.sku, l.qty FROM sales.order_line AS l WHERE l.order_id = o.order_id FOR JSON PATH));
            SELECT json_query(v.lines) AS v FROM @t AS v FOR JSON PATH;
            """);

        AssertLines(doc, Field(doc, "v").Type);
        Assert.Empty(doc.Unknowns);
    }

    [Theory]
    [InlineData("INSERT @t (id) VALUES (1);", "default")]
    [InlineData("UPDATE @t SET lines = @raw;", "@raw")]
    [InlineData("WITH c AS (SELECT t.id, t.lines FROM @t AS t) UPDATE c SET lines = N'x';", "CTE")]
    [InlineData("MERGE @t AS t USING sales.[order] AS o ON t.id = o.order_id WHEN NOT MATCHED THEN INSERT (id, lines) VALUES (o.order_id, o.notes);", "o.notes")]
    [InlineData("MERGE @t AS t USING sales.[order] AS o ON t.id = o.order_id WHEN MATCHED THEN UPDATE SET lines = o.notes;", "o.notes")]
    [InlineData("INSERT @t (id, lines) OUTPUT inserted.id, inserted.lines INTO @t (id, lines) VALUES (2, N'x');", "not valid JSON")]
    // OUTPUT … INTO a table variable writes it as an INSERT would; ScriptDom reads the target as a variable's reference.
    [InlineData("UPDATE sales.[order] SET total = 0 OUTPUT inserted.order_id, inserted.notes INTO @t;", "inserted.notes")]
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
    public void Output_into_a_table_variable_puts_the_json_it_writes_there()
    {
        var doc = Infer("""
            DECLARE @t TABLE (id int NOT NULL, lines nvarchar(max) NULL);
            DELETE FROM sales.[order] OUTPUT deleted.order_id, (SELECT l.sku, l.qty FROM sales.order_line AS l WHERE l.order_id = deleted.order_id FOR JSON PATH) INTO @t;
            SELECT json_query(t.lines) AS lines FROM @t AS t FOR JSON PATH;
            """);

        AssertLines(doc, Field(doc, "lines").Type);
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
}
