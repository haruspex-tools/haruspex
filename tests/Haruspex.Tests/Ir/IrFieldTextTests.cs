using Haruspex.Ir;

namespace Haruspex.Tests.Ir;

/// <summary>
/// Which fields are surprises (<see cref="IrSurprises"/>), the lines that explain one in generated code (<see cref="IrFieldText"/>),
/// and the path a person names a field by (<see cref="IrFieldPlaces"/>), on the invented <c>shop.order_summary</c> and its kin.
/// </summary>
public class IrFieldTextTests
{
    private const string Tables = """
        CREATE TABLE shop.customer_order (order_id int NOT NULL PRIMARY KEY, note nvarchar(40) NULL, placed_at datetime2(0) NOT NULL);
        GO
        CREATE TABLE shop.order_line (line_id int NOT NULL PRIMARY KEY, order_id int NOT NULL, shipped_at datetime2(0) NULL, price decimal(9,2) NOT NULL);
        """;

    private const string OrderSummary = """
        CREATE PROCEDURE shop.order_summary @order_id int AS
        SELECT count(l.line_id) AS line_count, sum(l.price) AS total, max(l.shipped_at) AS last_shipped, o.note, o.placed_at
        FROM shop.customer_order AS o LEFT JOIN shop.order_line AS l ON l.order_id = o.order_id
        WHERE o.order_id = @order_id;
        """;

    private static IrDocument Infer(string procedures = OrderSummary) =>
        InferProcedure.Infer("shop.*", ("tables.sql", Tables), ("procedures.sql", procedures));

    private static IrFieldPlace Place(IrDocument document, string path, string id = "shop.order_summary") =>
        IrFieldPlaces.Find(document, document.Objects.Single(o => o.Id == id), path) ?? throw new InvalidOperationException($"No field {path}.");

    private static IReadOnlyList<string> Lines(IrDocument document, string path, IrComments comments = IrComments.Surprises, string id = "shop.order_summary")
    {
        var place = Place(document, path, id);
        return IrFieldText.Of(document, place.TypeName, place.Field, comments);
    }

    [Fact]
    public void A_field_as_its_column_declares_is_no_surprise_and_a_rule_or_a_widening_is()
    {
        var document = Infer();

        Assert.False(IrSurprises.Of(Place(document, "note").Field));
        Assert.False(IrSurprises.Of(Place(document, "placed_at").Field));
        Assert.True(IrSurprises.Of(Place(document, "line_count").Field));
        Assert.True(IrSurprises.TypeDiffers(Place(document, "total").Field));
    }

    [Fact]
    public void A_key_left_out_because_its_column_is_nullable_is_no_surprise_but_one_a_rule_made_nullable_is()
    {
        // FOR JSON leaves out every null key, which the module's header says once; only a rule that made a NOT NULL column
        // nullable on its way to the key is particular to the field.
        var json = Infer("""
            CREATE PROCEDURE shop.order_note AS
            SELECT o.order_id, o.note, l.shipped_at, l.line_id AS first_line
            FROM shop.customer_order AS o LEFT JOIN shop.order_line AS l ON l.order_id = o.order_id FOR JSON PATH;
            """);
        var note = Place(json, "note", "shop.order_note").Field;
        var line = Place(json, "first_line", "shop.order_note").Field;

        Assert.True(IrSurprises.AbsentWhenNull(note));
        Assert.False(IrSurprises.Of(note));
        Assert.True(IrSurprises.Of(note with { Presence = IrPresence.Optional }));
        Assert.True(IrSurprises.Of(Place(json, "shipped_at", "shop.order_note").Field));
        Assert.True(IrSurprises.Of(line));
        var lines = Lines(json, "first_line", id: "shop.order_note");
        Assert.Equal(
            [
                "May be null: it comes from the optional side of an outer join, which has no row when nothing matches (unmatched-row).",
                "Left out when null, as FOR JSON leaves out a key whose value is null.",
            ],
            lines.Take(2));
        Assert.Equal("Why: haruspex explain shop.order_note first_line", lines[^1]);
        Assert.Contains("Left out when null, as FOR JSON leaves out a key whose value is null.", Lines(json, "note", IrComments.All, "shop.order_note"));
    }

    [Fact]
    public void A_surprise_is_explained_by_its_reasons_codes_the_way_out_and_the_command_with_no_location()
    {
        Assert.Equal(
            [
                "May be null: COUNT is an int, null past 2,147,483,647 rows when the caller's session has ANSI_WARNINGS and ARITHABORT off (count-overflow).",
                "To change it: use COUNT_BIG, or declare the callers' ANSI_WARNINGS or ARITHABORT on in the config file's \"session\".",
                "Why: haruspex explain shop.order_summary line_count",
            ],
            Lines(Infer(), "line_count"));
    }

    [Fact]
    public void A_type_none_of_its_columns_has_is_said_with_each_column_s_type() =>
        Assert.Equal("Its type is decimal(38,2), where shop.order_line.price is decimal(9,2): what it is computed with makes it another type.", Lines(Infer(), "total")[0]);

    [Fact]
    public void Under_none_nothing_is_explained_and_under_all_every_field_is()
    {
        var document = Infer();

        Assert.Empty(Lines(document, "line_count", IrComments.None));
        Assert.Empty(Lines(document, "note"));
        Assert.Equal(
            [
                "Read from shop.customer_order.note.",
                "May be null: the column it reads is declared nullable (nullable-column).",
                "To change it: if the data rules it out, declare it in the config file with \"nullable\": false and why.",
                "Why: haruspex explain shop.order_summary note",
            ],
            Lines(document, "note", IrComments.All));
    }

    [Fact]
    public void A_result_that_may_be_missing_is_explained_on_its_object_and_its_rows_under_all()
    {
        var document = Infer("CREATE PROCEDURE shop.order_json @order_id int AS SELECT o.order_id FROM shop.customer_order AS o WHERE o.order_id = @order_id FOR JSON PATH;");
        var obj = document.Objects.Single();

        Assert.Equal(
            ["Its result may be missing: the query returns nothing when no rows match (no-rows).", "Why: haruspex explain shop.order_json"],
            IrFieldText.Of(document, obj, IrComments.Surprises));
        Assert.Contains("Its result has at most one row: TOP (1), FETCH NEXT 1, or a filter that fixes a key (atMostOne).", IrFieldText.Of(document, obj, IrComments.All));
    }

    [Fact]
    public void A_field_is_named_inside_its_set_by_the_set_s_name_or_place_and_inside_json_by_its_path()
    {
        var one = Infer();
        var two = Infer("CREATE PROCEDURE shop.two AS SELECT o.order_id FROM shop.customer_order AS o; SELECT l.line_id FROM shop.order_line AS l;");
        var nested = Infer("CREATE PROCEDURE shop.nested AS SELECT o.order_id, (SELECT l.line_id FROM shop.order_line AS l WHERE l.order_id = o.order_id FOR JSON PATH) AS lines FROM shop.customer_order AS o FOR JSON PATH;");

        Assert.Equal(["line_count", "total", "last_shipped", "note", "placed_at"], IrFieldPlaces.Fields(one, one.Objects.Single()).Select(p => p.Path));
        Assert.Equal(["#1.order_id", "#2.line_id"], IrFieldPlaces.Fields(two, two.Objects.Single()).Select(p => p.Path).Order(StringComparer.Ordinal));
        Assert.Equal("lines[].line_id", Place(nested, "lines.line_id", "shop.nested").Path);
        Assert.Equal("haruspex explain shop.two '#2.line_id'", IrFieldPlaces.Command(two, "shop.two", "results[1][].line_id"));
        Assert.Null(IrFieldPlaces.Command(two, "shop.two", "results[1][].nothing"));
    }

    [Fact]
    public void A_name_holding_a_comment_s_end_is_quoted_so_the_command_runs_as_written_and_ends_no_comment()
    {
        // To a shell, 'shop.we*'"/"'ird' is the one word shop.we*/ird.
        Assert.Equal("'shop.we*'\"/\"'ird'", IrFieldPlaces.ShellWord("shop.we*/ird"));
        Assert.Equal("'#2.it'\\''s'", IrFieldPlaces.ShellWord("#2.it's"));
        Assert.Equal("shop.order_summary", IrFieldPlaces.ShellWord("shop.order_summary"));
        Assert.DoesNotContain("*/", IrFieldPlaces.ShellWord("a*/b*/c"), StringComparison.Ordinal);
    }
}
