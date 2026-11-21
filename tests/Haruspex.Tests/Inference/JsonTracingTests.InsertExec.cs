using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

/// <summary>
/// JSON a called procedure's result columns hold, carried by <c>INSERT … EXEC</c> into a table variable's columns. The rules
/// are observed in the differential tests' <c>ExecObservationTests</c> ("INSERT … EXEC of JSON").
/// </summary>
public partial class JsonTracingTests
{
    /// <summary>
    /// A callee returning, by position, an id, an object (<c>head</c>) and an array of lines that is never null (the
    /// <c>coalesce</c> fallback), as a plain rowset.
    /// </summary>
    private const string Cards = """
        CREATE PROCEDURE sales.cards @id int AS
        SELECT o.order_id, json_object('total': o.total) AS head,
            json_query(coalesce((SELECT l.sku, l.qty FROM sales.order_line AS l WHERE l.order_id = o.order_id FOR JSON PATH), N'[]')) AS lines
        FROM sales.[order] AS o WHERE o.order_id = @id;
        GO
        """;

    private const string Rows = "DECLARE @r TABLE (order_id int PRIMARY KEY, head nvarchar(max) NULL, lines nvarchar(max) NULL);\n";

    /// <summary>The head's type: an object with one key, total.</summary>
    private static void AssertHead(IrDocument doc, IrType type) => Assert.Equal(["total"], Fields(doc, type).Select(f => f.Name));

    [Fact]
    public void Insert_exec_carries_the_json_of_the_callees_columns_into_a_table_variable()
    {
        var doc = Infer(Rows + """
            INSERT INTO @r EXEC sales.cards @id;
            SELECT r.order_id AS id, json_query(r.head) AS head, json_query(r.lines) AS lines FROM @r AS r FOR JSON PATH;
            """, more: Cards);

        AssertHead(doc, Field(doc, "head").Type);
        AssertLines(doc, Field(doc, "lines").Type);
        Assert.Empty(doc.Unknowns);
    }

    [Fact]
    public void Insert_exec_maps_the_callees_columns_by_position_not_by_name()
    {
        // Observed: Insert_exec_maps_the_callees_columns_to_the_targets_by_position. The column list names lines second, so the
        // callee's second column, the head object, goes into it; other, left out, keeps its default, which is not read.
        var doc = Infer("""
            DECLARE @r TABLE (order_id int PRIMARY KEY, other nvarchar(max) NULL, head nvarchar(max) NULL, lines nvarchar(max) NULL);
            INSERT INTO @r (order_id, lines, head) EXEC sales.cards @id;
            SELECT json_query(r.head) AS head, json_query(r.lines) AS lines, json_query(r.other) AS other FROM @r AS r FOR JSON PATH;
            """, more: Cards);

        AssertLines(doc, Field(doc, "head").Type);
        AssertHead(doc, Field(doc, "lines").Type);
        Assert.Contains("left out", Unknown(doc, "other").Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Insert_exec_without_a_column_list_skips_the_table_variables_identity_column()
    {
        var doc = Infer("""
            DECLARE @r TABLE (n int IDENTITY(1, 1) NOT NULL, order_id int NOT NULL, head nvarchar(max) NULL, lines nvarchar(max) NULL);
            INSERT INTO @r EXEC sales.cards @id;
            SELECT json_query(r.head) AS head, json_query(r.lines) AS lines FROM @r AS r FOR JSON PATH;
            """, more: Cards);

        AssertHead(doc, Field(doc, "head").Type);
        AssertLines(doc, Field(doc, "lines").Type);
        Assert.Empty(doc.Unknowns);
    }

    [Theory]
    [InlineData("DECLARE @r TABLE")]
    [InlineData("CREATE TABLE #r")]
    public void Insert_exec_without_a_column_list_skips_a_computed_column(string table)
    {
        // Observed: Insert_exec_maps_the_callees_columns_to_the_targets_by_position. A computed column takes no value, so the
        // callee's three columns go into the other three.
        var name = table.EndsWith("#r", StringComparison.Ordinal) ? "#r" : "@r";
        var doc = Infer($"""
            {table} (order_id int NOT NULL, twice AS order_id * 2, head nvarchar(max) NULL, lines nvarchar(max) NULL);
            INSERT INTO {name} EXEC sales.cards @id;
            SELECT json_query(r.head) AS head, json_query(r.lines) AS lines FROM {name} AS r FOR JSON PATH;
            """, more: Cards);

        AssertHead(doc, Field(doc, "head").Type);
        AssertLines(doc, Field(doc, "lines").Type);
        Assert.Empty(doc.Unknowns);
    }

    [Fact]
    public void Insert_exec_joins_what_every_result_set_of_the_callee_inserts()
    {
        // Observed: Insert_exec_inserts_the_rows_of_every_result_set_and_returns_none_to_the_client. The second result set puts
        // an object where the first put lines, so the column holds either.
        var doc = Infer(Rows + """
            INSERT INTO @r EXEC sales.two_sets @id;
            SELECT json_query(r.lines) AS lines FROM @r AS r FOR JSON PATH;
            """, more: Cards + "\n" + """
            CREATE PROCEDURE sales.two_sets @id int AS
            EXEC sales.cards @id;
            SELECT o.order_id, NULL AS head, json_object('total': o.total) AS lines FROM sales.[order] AS o;
            GO
            """);

        var union = Assert.IsType<IrUnion>(Field(doc, "lines").Type);
        Assert.Equal(2, union.Variants.Count);
        Assert.Contains(union.Variants, v => v is IrArray);
        Assert.Empty(doc.Unknowns);
    }

    [Fact]
    public void Insert_exec_joins_what_each_path_of_the_callee_inserts()
    {
        var doc = Infer(Rows + """
            INSERT INTO @r EXEC sales.either @id;
            SELECT json_query(r.lines) AS lines FROM @r AS r FOR JSON PATH;
            """, more: Cards + "\n" + """
            CREATE PROCEDURE sales.either @id int AS
            IF @id = 1 BEGIN EXEC sales.cards @id; END
            ELSE SELECT o.order_id, NULL AS head, o.notes FROM sales.[order] AS o;
            GO
            """);

        Assert.Contains("notes", Unknown(doc, "lines").Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Insert_exec_of_a_callee_that_runs_for_json_keeps_only_what_its_rowsets_insert()
    {
        // Observed: Insert_exec_of_a_procedure_that_runs_for_json_fails. The FOR JSON path fails the insert, so it puts nothing in
        // the table, and the content is the rowset's.
        var doc = Infer(Rows + """
            INSERT INTO @r EXEC sales.json_first @id;
            SELECT json_query(r.lines) AS lines FROM @r AS r FOR JSON PATH;
            """, more: Cards + "\n" + """
            CREATE PROCEDURE sales.json_first @id int AS
            IF @id = 1 SELECT o.order_id, o.notes, o.notes FROM sales.[order] AS o FOR JSON PATH;
            EXEC sales.cards @id;
            GO
            """);

        AssertLines(doc, Field(doc, "lines").Type);
        Assert.Empty(doc.Unknowns);
    }

    [Fact]
    public void Insert_exec_keeps_what_the_callee_returns_before_an_error_its_own_catch_takes()
    {
        // Observed: Insert_exec_whose_callee_fails_inserts_nothing_unless_the_error_lets_it_go_on. The rows the TRY returned
        // before the error stay, beside the CATCH's.
        var doc = Infer(Rows + """
            INSERT INTO @r EXEC sales.caught @id;
            SELECT json_query(r.lines) AS lines FROM @r AS r FOR JSON PATH;
            """, more: Cards + "\n" + """
            CREATE PROCEDURE sales.caught @id int AS
            BEGIN TRY
                EXEC sales.cards @id;
                RAISERROR (N'failed', 16, 1);
            END TRY
            BEGIN CATCH
                SELECT 0 AS order_id, NULL AS head, json_object('total': 0) AS lines;
            END CATCH
            GO
            """);

        var union = Assert.IsType<IrUnion>(Field(doc, "lines").Type);
        Assert.Contains(union.Variants, v => v is IrArray);
        Assert.Empty(doc.Unknowns);
    }

    [Theory]
    [InlineData("EXEC sp_executesql N'SELECT 1, NULL, NULL'", "dynamic SQL")]
    [InlineData("EXEC (N'SELECT 1, NULL, NULL')", "dynamic SQL")]
    [InlineData("EXEC other_db.sales.cards @id", "other_db")]
    [InlineData("EXEC sales.missing @id", "sales.missing")]
    public void Insert_exec_of_a_callee_whose_results_are_not_known_leaves_the_columns_unknown(string call, string named)
    {
        // A JSON array written first: a call not followed must not leave it standing.
        var doc = Infer(Rows + $"""
            INSERT @r (order_id, lines) VALUES (0, N'[]');
            INSERT INTO @r {call};
            SELECT json_query(r.lines) AS lines FROM @r AS r FOR JSON PATH;
            """, more: Cards);

        var unknown = Unknown(doc, "lines");
        Assert.Equal(IrUnknownReason.JsonShapeUnknown, unknown.Reason);
        Assert.Contains(named, unknown.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Insert_exec_of_a_callee_column_that_is_not_json_built_there_leaves_the_column_unknown()
    {
        var doc = Infer(Rows + """
            INSERT INTO @r EXEC sales.notes @id;
            SELECT json_query(r.lines) AS lines FROM @r AS r FOR JSON PATH;
            """, more: """
            CREATE PROCEDURE sales.notes @id int AS SELECT o.order_id, o.notes, o.notes FROM sales.[order] AS o;
            GO
            """);

        Assert.Equal(IrUnknownReason.JsonShapeUnknown, Unknown(doc, "lines").Reason);
    }

    [Theory]
    [InlineData("nvarchar(4000)")]
    [InlineData("varchar(max)")]
    [InlineData("nvarchar(10)")]
    public void Insert_exec_into_a_column_that_may_change_the_text_leaves_its_json_unknown(string type)
    {
        // Observed: Insert_exec_converts_json_to_the_columns_type_as_insert_does. A shorter column refuses longer text (8152) and
        // varchar loses the characters its code page lacks, so only nvarchar(max) holds the text unchanged.
        var doc = Infer($"""
            DECLARE @r TABLE (order_id int PRIMARY KEY, head {type} NULL, lines {type} NULL);
            INSERT INTO @r EXEC sales.cards @id;
            SELECT json_query(r.lines) AS lines FROM @r AS r FOR JSON PATH;
            """, more: Cards);

        Assert.IsType<IrUnknownType>(Field(doc, "lines").Type);
    }

    [Fact]
    public void Json_carried_by_insert_exec_is_a_string_unless_read_with_json_query()
    {
        // Observed: Json_inserted_by_insert_exec_is_text_until_json_query_reads_it. Stored in a column it is text: a plain read
        // is a string, and FOR JSON nests it as a quoted string.
        var doc = Infer(Rows + """
            INSERT INTO @r EXEC sales.cards @id;
            SELECT r.lines AS plain, json_query(r.lines) AS parsed FROM @r AS r FOR JSON PATH;
            SELECT r.lines AS text FROM @r AS r;
            """, more: Cards);

        Assert.IsType<IrJsonText>(Field(doc, "plain").Type);
        AssertLines(doc, Field(doc, "parsed").Type);
        Assert.Empty(doc.Unknowns);
    }

    [Fact]
    public void A_null_json_column_inserted_by_insert_exec_reads_as_null()
    {
        // Observed: Insert_exec_of_no_rows_inserts_none_and_a_null_json_column_stays_null.
        var doc = Infer(Rows + """
            INSERT INTO @r EXEC sales.maybe @id;
            SELECT json_query(r.head) AS head FROM @r AS r FOR JSON PATH, INCLUDE_NULL_VALUES;
            """, more: """
            CREATE PROCEDURE sales.maybe @id int AS
            SELECT o.order_id, CASE WHEN o.total > 0 THEN json_object('total': o.total) END AS head, NULL AS lines FROM sales.[order] AS o;
            GO
            """);

        AssertHead(doc, Field(doc, "head").Type);
        Assert.True(Field(doc, "head").Nullable);
    }

    [Theory]
    [InlineData("UPDATE @r SET lines = @raw;", "@raw")]
    [InlineData("INSERT @r (order_id, lines) VALUES (0, N'x');", "not valid JSON")]
    [InlineData("IF @flag = 1 INSERT @r (order_id, lines) VALUES (0, @raw);", "@raw")]
    public void A_later_write_joins_with_what_insert_exec_carried(string write, string named)
    {
        var doc = Infer(Rows + $"""
            INSERT INTO @r EXEC sales.cards @id;
            {write}
            SELECT json_query(r.lines) AS lines FROM @r AS r FOR JSON PATH;
            """, more: Cards);

        Assert.Contains(named, Unknown(doc, "lines").Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Insert_exec_on_one_branch_or_in_a_loop_joins_with_what_the_table_held()
    {
        var doc = Infer(Rows + """
            IF @flag = 1 INSERT INTO @r EXEC sales.cards @id;
            WHILE @id > 0
            BEGIN
                INSERT INTO @r EXEC sales.cards @id;
                SET @id = @id - 1;
            END
            SELECT json_query(r.lines) AS lines FROM @r AS r FOR JSON PATH;
            """, more: Cards);

        AssertLines(doc, Field(doc, "lines").Type);
        Assert.Empty(doc.Unknowns);
    }

    [Fact]
    public void A_nested_insert_exec_inserts_nothing()
    {
        // Observed: Insert_exec_cannot_be_nested. The callee always fails, so the table holds what it held before.
        var doc = Infer(Rows + """
            INSERT @r (order_id, lines) VALUES (0, N'[]');
            BEGIN TRY INSERT INTO @r EXEC sales.nests @id; END TRY BEGIN CATCH PRINT N'failed'; END CATCH
            SELECT json_query(r.lines) AS lines FROM @r AS r FOR JSON PATH;
            """, more: """
            CREATE PROCEDURE sales.nests @id int AS
            DECLARE @inner TABLE (order_id int, head nvarchar(max), lines nvarchar(max));
            INSERT INTO @inner EXEC sales.notes @id;
            SELECT i.order_id, i.head, i.lines FROM @inner AS i;
            GO
            CREATE PROCEDURE sales.notes @id int AS SELECT o.order_id, o.notes, o.notes FROM sales.[order] AS o;
            GO
            """);

        Assert.Empty(Assert.IsType<IrTuple>(Field(doc, "lines").Type).Items);
        Assert.Empty(doc.Unknowns);
    }
}
