using Haruspex.Cli;
using Haruspex.Emit.Zod;
using Haruspex.Ir;

namespace Haruspex.Tests.Inference;

/// <summary>
/// Scalar functions that return JSON, and functions that call themselves. Every rule here was observed on SQL Server
/// 2025 first (JsonFunctionObservationTests in the differential tests): a plain call is escaped text, json_query over
/// it or a json return type embeds it live, FOR JSON over no rows returns NULL, and a key whose json_query is NULL is
/// left out of FOR JSON PATH.
/// </summary>
public class JsonFunctionTests
{
    private const string Tables = """
        CREATE TABLE dbo.t (id int NOT NULL PRIMARY KEY, parent_id int NULL, name nvarchar(10) NOT NULL, note nvarchar(20) NULL);
        GO
        """;

    private static IrDocument Infer(string functions, string body) =>
        InferProcedure.Infer("dbo.p", ("tables.sql", Tables), ("functions.sql", functions), ("p.sql", $"CREATE PROCEDURE dbo.p AS\n{body}"));

    private static IrField Field(IrDocument doc, string name) => Fields(doc, Row(doc)).Single(f => f.Name == name);

    private static IrRef Row(IrDocument doc) => doc.Objects[0].Results![0].Type switch
    {
        IrArray { Items: IrRef r } => r,
        IrRef r => r,
        var other => throw new InvalidOperationException($"Unexpected result type {other}."),
    };

    // Stricter than IrNavigation.Fields: the type must be an object type, not an array of one.
    private static IReadOnlyList<IrField> Fields(IrDocument doc, IrType type) => doc.Types[Assert.IsType<IrRef>(type).Name].Fields;

    private static IrField Sub(IrDocument doc, IrType type, string name) => Fields(doc, type).Single(f => f.Name == name);

    private static IEnumerable<string> Rules(IEnumerable<IrNullReason> reasons) => reasons.Select(r => r.Rule);

    private const string Children = """
        CREATE FUNCTION dbo.children(@id int) RETURNS nvarchar(max) AS
        BEGIN
            RETURN (SELECT c.id, c.name FROM dbo.t AS c WHERE c.parent_id = @id FOR JSON PATH);
        END
        GO
        """;

    // ---- non-recursive functions ----------------------------------------------------------------------

    [Fact]
    public void A_plain_call_of_a_for_json_function_is_text_whose_content_is_known()
    {
        var doc = Infer(Children, "SELECT t.id, dbo.children(t.id) AS kids FROM dbo.t AS t FOR JSON PATH;");
        var kids = Field(doc, "kids");

        // Observed: [{"x":"[{\"id\":2,…}]"}]. Escaped, so a string; NULL over no rows, so the key is left out.
        var text = Assert.IsType<IrJsonText>(kids.Type);
        var items = Assert.IsType<IrArray>(text.Content);
        Assert.Equal(["id", "name"], Fields(doc, items.Items).Select(f => f.Name));
        Assert.Equal(["empty-subquery"], Rules(kids.NullableBecause));
        Assert.Equal(IrPresence.OmittedWhenNull, kids.Presence);
        Assert.Empty(doc.Unknowns);
    }

    [Fact]
    public void Json_query_over_the_call_embeds_it_live()
    {
        var doc = Infer(Children, "SELECT t.id, json_query(dbo.children(t.id)) AS kids FROM dbo.t AS t FOR JSON PATH;");
        var kids = Field(doc, "kids");

        var items = Assert.IsType<IrArray>(kids.Type);
        var id = Sub(doc, items.Items, "id");
        Assert.False(id.Nullable);
        Assert.Equal(IrFormats.Int32, Assert.IsType<IrScalar>(id.Type).Format);
        Assert.Equal(["empty-subquery"], Rules(kids.NullableBecause));
        Assert.Equal(IrPresence.OmittedWhenNull, kids.Presence);
    }

    [Fact]
    public void A_constructor_returned_is_never_null()
    {
        var doc = Infer(
            """
            CREATE FUNCTION dbo.card(@id int, @name nvarchar(10)) RETURNS nvarchar(max) AS
            BEGIN
                RETURN json_object('id': @id, 'name': @name);
            END
            GO
            """,
            "SELECT json_query(dbo.card(t.id, t.name)) AS live, dbo.card(t.id, t.name) AS text FROM dbo.t AS t FOR JSON PATH;");

        var live = Field(doc, "live");
        Assert.False(live.Nullable);
        Assert.Equal(IrPresence.Always, live.Presence);

        // Inside the function a parameter can be null whatever the caller passes, so its keys are nullable.
        var id = Sub(doc, live.Type, "id");
        Assert.Equal(["parameter"], Rules(id.NullableBecause));
        Assert.Equal(IrPresence.Always, id.Presence);
        Assert.Equal(IrFormats.Int32, Assert.IsType<IrScalar>(id.Type).Format);

        // The argument has been converted to the parameter's type (cut short, rounded: observed), so its facts hold.
        Assert.Equal(10, Assert.IsType<IrScalar>(Sub(doc, live.Type, "name").Type).Facets!.Length!.Value);

        var text = Field(doc, "text");
        Assert.False(text.Nullable);
        Assert.IsType<IrRef>(Assert.IsType<IrJsonText>(text.Type).Content);
    }

    [Fact]
    public void A_json_return_type_embeds_live_without_json_query()
    {
        var doc = Infer(
            """
            CREATE FUNCTION dbo.card(@id int) RETURNS json AS
            BEGIN
                RETURN json_object('id': @id);
            END
            GO
            """,
            "SELECT dbo.card(t.id) AS card FROM dbo.t AS t FOR JSON PATH;");

        Assert.Equal("id", Assert.Single(Fields(doc, Field(doc, "card").Type)).Name);
    }

    [Fact]
    public void In_a_rowset_a_json_function_is_text_with_its_content()
    {
        var doc = Infer(Children, "SELECT t.id, dbo.children(t.id) AS kids, json_query(dbo.children(t.id)) AS live FROM dbo.t AS t;");

        Assert.IsType<IrArray>(Assert.IsType<IrJsonText>(Field(doc, "kids").Type).Content);
        Assert.IsType<IrArray>(Assert.IsType<IrJsonText>(Field(doc, "live").Type).Content);
    }

    [Theory]
    [InlineData("DECLARE @r nvarchar(max); SET @r = (SELECT c.id FROM dbo.t AS c WHERE c.parent_id = @id FOR JSON PATH); RETURN @r;")]
    [InlineData("DECLARE @r nvarchar(max) = (SELECT c.id FROM dbo.t AS c WHERE c.parent_id = @id FOR JSON PATH); RETURN @r;")]
    [InlineData("DECLARE @r nvarchar(max); SELECT @r = (SELECT c.id FROM dbo.t AS c WHERE c.parent_id = @id FOR JSON PATH); RETURN @r;")]
    [InlineData("DECLARE @r json; SET @r = N'[]'; SET @r = (SELECT c.id FROM dbo.t AS c WHERE c.parent_id = @id FOR JSON PATH); RETURN @r;")]
    public void A_variable_assigned_and_returned_gives_its_shape(string body)
    {
        var doc = Infer($"CREATE FUNCTION dbo.f(@id int) RETURNS nvarchar(max) AS\nBEGIN\n{body}\nEND\nGO\n", "SELECT json_query(dbo.f(t.id)) AS v FROM dbo.t AS t FOR JSON PATH;");
        var v = Field(doc, "v");

        Assert.Equal("id", Assert.Single(Fields(doc, Assert.IsType<IrArray>(v.Type).Items)).Name);
        Assert.Equal(["empty-subquery"], Rules(v.NullableBecause));
    }

    [Theory]
    // Control flow: which value is returned depends on data.
    [InlineData("nvarchar(max)", "IF @id > 0 RETURN json_object('a': 1); RETURN NULL;")]
    // A variable too short for the JSON truncates it (observed for a return type of nvarchar(10)).
    [InlineData("nvarchar(max)", "DECLARE @r nvarchar(10) = json_object('a': 1); RETURN @r;")]
    // A variable read before the return may have been assigned in a way not followed.
    [InlineData("nvarchar(max)", "DECLARE @r nvarchar(max); SET @r += json_object('a': 1); RETURN @r;")]
    // A parameter assigned in the body no longer holds what the caller passed.
    [InlineData("nvarchar(max)", "SET @id = 1; RETURN json_object('a': @id);")]
    // A return type that cannot hold every JSON text cuts it short.
    [InlineData("nvarchar(4000)", "RETURN json_object('a': 1);")]
    [InlineData("varchar(max)", "RETURN json_object('a': 1);")]
    // Not JSON at all.
    [InlineData("nvarchar(max)", "RETURN N'plain';")]
    // A SELECT that may assign nothing leaves the variable NULL (observed for TOP (0) and HAVING 1 = 0).
    [InlineData("nvarchar(max)", "DECLARE @r nvarchar(max); SELECT TOP (0) @r = json_object('a': 1); RETURN @r;")]
    [InlineData("nvarchar(max)", "DECLARE @r nvarchar(max); SELECT @r = json_object('a': 1) HAVING 1 = 0; RETURN @r;")]
    [InlineData("nvarchar(max)", "DECLARE @r nvarchar(max); SELECT @r = json_object('a': 1) ORDER BY (SELECT 1) OFFSET 1 ROWS; RETURN @r;")]
    [InlineData("nvarchar(max)", "DECLARE @r nvarchar(max); SELECT @r = json_object('a': 1) GROUP BY (); RETURN @r;")]
    public void Any_other_body_gives_the_declared_return_type(string returns, string body)
    {
        var doc = Infer($"CREATE FUNCTION dbo.f(@id int) RETURNS {returns} AS\nBEGIN\n{body}\nEND\nGO\n", "SELECT dbo.f(t.id) AS v, json_query(dbo.f(t.id)) AS q FROM dbo.t AS t FOR JSON PATH;");

        var v = Field(doc, "v");
        Assert.Equal(IrScalarKind.String, Assert.IsType<IrScalar>(v.Type).Kind);
        Assert.Equal(["function-result"], Rules(v.NullableBecause));
        Assert.Equal(IrUnknownReasons.JsonShapeUnknown, Assert.IsType<IrUnknownType>(Field(doc, "q").Type).Reason);
    }

    [Fact]
    public void A_function_of_another_type_gives_its_declared_type_and_may_be_null()
    {
        var doc = Infer(
            "CREATE FUNCTION dbo.twice(@n int) RETURNS int AS BEGIN RETURN @n * 2; END\nGO\n",
            "SELECT dbo.twice(t.id) AS v FROM dbo.t AS t FOR JSON PATH;");
        var v = Field(doc, "v");

        Assert.Equal(IrFormats.Int32, Assert.IsType<IrScalar>(v.Type).Format);
        Assert.Equal(["function-result"], Rules(v.NullableBecause));
    }

    [Fact]
    public void Returns_null_on_null_input_makes_parameters_non_null_and_a_nullable_argument_null()
    {
        // Observed: f(NULL) is NULL without running the body.
        var doc = Infer(
            """
            CREATE FUNCTION dbo.card(@id int) RETURNS nvarchar(max) WITH RETURNS NULL ON NULL INPUT AS
            BEGIN
                RETURN json_object('id': @id);
            END
            GO
            """,
            "SELECT json_query(dbo.card(t.id)) AS sure, json_query(dbo.card(t.parent_id)) AS maybe FROM dbo.t AS t FOR JSON PATH;");

        var sure = Field(doc, "sure");
        Assert.False(sure.Nullable);
        Assert.False(Sub(doc, sure.Type, "id").Nullable);
        var maybe = Field(doc, "maybe");
        Assert.Equal(["null-input"], Rules(maybe.NullableBecause));
        Assert.Equal(IrPresence.OmittedWhenNull, maybe.Presence);
    }

    [Fact]
    public void A_function_not_in_the_project_is_unknown()
    {
        var doc = Infer(Children, "SELECT dbo.missing(t.id) AS v FROM dbo.t AS t FOR JSON PATH;");

        Assert.Single(doc.Unknowns, u => u.Path == "results[0][].v");
    }

    [Fact]
    public void A_function_also_altered_is_unknown()
    {
        var doc = Infer(Children + "\nALTER FUNCTION dbo.children(@id int) RETURNS int AS BEGIN RETURN 1; END\nGO\n", "SELECT dbo.children(t.id) AS v FROM dbo.t AS t FOR JSON PATH;");

        Assert.Equal(IrUnknownReasons.Redefined, Assert.Single(doc.Unknowns, u => u.Path == "results[0][].v").Reason);
    }

    // ---- recursion -------------------------------------------------------------------------------------

    private const string Tree = """
        CREATE FUNCTION dbo.tree(@id int) RETURNS nvarchar(max) AS
        BEGIN
            RETURN (
                SELECT c.id, c.name, c.note, json_query(dbo.tree(c.id)) AS children
                FROM dbo.t AS c WHERE c.parent_id = @id
                FOR JSON PATH);
        END
        GO
        """;

    [Fact]
    public void A_function_that_calls_itself_is_a_recursive_named_type()
    {
        var doc = Infer(Tree, "SELECT t.id, json_query(dbo.tree(t.id)) AS children FROM dbo.t AS t WHERE t.parent_id IS NULL FOR JSON PATH;");

        var children = Field(doc, "children");
        var node = Assert.IsType<IrRef>(Assert.IsType<IrArray>(children.Type).Items);
        Assert.Equal("TreeNode", node.Name);
        Assert.True(doc.Types["TreeNode"].Recursive);
        Assert.False(doc.Types[Row(doc).Name].Recursive);

        // A leaf's children are NULL (FOR JSON over no rows), so the key is left out (observed).
        var inner = Sub(doc, node, "children");
        Assert.Equal("TreeNode", Assert.IsType<IrRef>(Assert.IsType<IrArray>(inner.Type).Items).Name);
        Assert.Equal(IrPresence.OmittedWhenNull, inner.Presence);
        Assert.Equal(["empty-subquery"], Rules(inner.NullableBecause));
        Assert.Equal(IrPresence.OmittedWhenNull, Sub(doc, node, "note").Presence);
        Assert.False(Sub(doc, node, "id").Nullable);
        Assert.Empty(doc.Unknowns);
    }

    [Fact]
    public void A_plain_recursive_call_is_text_whose_content_is_the_recursive_type()
    {
        var doc = Infer(Tree, "SELECT dbo.tree(t.id) AS children FROM dbo.t AS t FOR JSON PATH;");

        var content = Assert.IsType<IrArray>(Assert.IsType<IrJsonText>(Field(doc, "children").Type).Content);
        Assert.Equal("TreeNode", Assert.IsType<IrRef>(content.Items).Name);
    }

    private const string Categories = """
        CREATE FUNCTION dbo.categories(@parent int) RETURNS nvarchar(max) AS
        BEGIN
            RETURN (
                SELECT c.id, json_query(dbo.items(c.id)) AS items
                FROM dbo.t AS c WHERE c.parent_id = @parent
                FOR JSON PATH);
        END
        GO
        CREATE FUNCTION dbo.items(@category int) RETURNS nvarchar(max) AS
        BEGIN
            RETURN json_object('id': @category, 'subcategories': json_query(dbo.categories(@category)) ABSENT ON NULL);
        END
        GO
        """;

    [Fact]
    public void Mutually_recursive_functions_are_each_a_recursive_named_type()
    {
        var doc = Infer(Categories, "SELECT json_query(dbo.categories(t.id)) AS tree FROM dbo.t AS t FOR JSON PATH;");

        var category = Assert.IsType<IrRef>(Assert.IsType<IrArray>(Field(doc, "tree").Type).Items);
        Assert.Equal("CategoriesNode", category.Name);
        var items = Sub(doc, category, "items");
        Assert.Equal("ItemsNode", Assert.IsType<IrRef>(items.Type).Name);

        // items() returns a constructor, never null, so its key is always there...
        Assert.False(items.Nullable);
        Assert.Equal(IrPresence.Always, items.Presence);

        // ...and categories() may be NULL, which ABSENT ON NULL leaves out: known only once the fixed point is reached.
        var sub = Sub(doc, items.Type, "subcategories");
        Assert.Equal("CategoriesNode", Assert.IsType<IrRef>(Assert.IsType<IrArray>(sub.Type).Items).Name);
        Assert.Equal(["empty-subquery"], Rules(sub.NullableBecause));
        Assert.Equal(IrPresence.OmittedWhenNull, sub.Presence);
        Assert.True(doc.Types["CategoriesNode"].Recursive);
        Assert.True(doc.Types["ItemsNode"].Recursive);
    }

    [Fact]
    public void Recursion_with_no_fixed_point_in_bounded_steps_is_unknown()
    {
        // Each step nests the tuple one level deeper, and a tuple is not a named type, so it never settles.
        var doc = Infer(
            "CREATE FUNCTION dbo.nest(@n int) RETURNS nvarchar(max) AS BEGIN RETURN json_array(@n, json_query(dbo.nest(@n - 1)) NULL ON NULL); END\nGO\n",
            "SELECT json_query(dbo.nest(t.id)) AS v FROM dbo.t AS t FOR JSON PATH;");

        Assert.Equal(IrUnknownReasons.UnresolvedRecursion, Assert.Single(doc.Unknowns, u => u.Path == "results[0][].v").Reason);
    }

    [Fact]
    public void A_function_whose_every_return_calls_itself_never_returns_and_is_unknown()
    {
        var doc = Infer(
            "CREATE FUNCTION dbo.loop(@n int) RETURNS nvarchar(max) AS BEGIN RETURN json_query(dbo.loop(@n)); END\nGO\n",
            "SELECT json_query(dbo.loop(t.id)) AS v FROM dbo.t AS t FOR JSON PATH;");

        Assert.Single(doc.Unknowns, u => u.Path == "results[0][].v");
    }

    [Fact]
    public void A_recursive_type_with_no_finite_value_is_unknown()
    {
        // Every call makes another, through a key always present and never null: the server always raises error 217.
        var doc = Infer(
            "CREATE FUNCTION dbo.forever(@x int) RETURNS nvarchar(max) AS BEGIN RETURN json_object('c': json_query(dbo.forever(@x))); END\nGO\n",
            "SELECT json_query(dbo.forever(t.id)) AS v FROM dbo.t AS t FOR JSON PATH;");

        Assert.Equal(IrUnknownReasons.UnresolvedRecursion, Assert.Single(doc.Unknowns, u => u.Path == "results[0][].v").Reason);
        Assert.DoesNotContain("ForeverNode", doc.Types.Keys);
    }

    [Fact]
    public void A_recursive_type_with_no_finite_value_through_a_plain_call_is_unknown()
    {
        // The call is text, but text holding a value that never ends: the server raises error 217 every time.
        var doc = Infer(
            "CREATE FUNCTION dbo.forever_text(@x int) RETURNS nvarchar(max) AS BEGIN RETURN json_object('c': dbo.forever_text(@x)); END\nGO\n",
            "SELECT json_query(dbo.forever_text(t.id)) AS v, dbo.forever_text(t.id) AS w FROM dbo.t AS t FOR JSON PATH;");

        Assert.Equal(IrUnknownReasons.UnresolvedRecursion, Assert.Single(doc.Unknowns, u => u.Path == "results[0][].v").Reason);
        Assert.Null(Assert.IsType<IrJsonText>(Field(doc, "w").Type).Content);
        Assert.DoesNotContain("ForeverTextNode", doc.Types.Keys);
    }

    [Fact]
    public void A_recursive_type_whose_cycle_can_end_in_null_is_kept()
    {
        // A null argument makes the call NULL, which is where a chain ends; ABSENT ON NULL then leaves the key out.
        var doc = Infer(
            "CREATE FUNCTION dbo.chain(@x int) RETURNS nvarchar(max) WITH RETURNS NULL ON NULL INPUT AS BEGIN RETURN json_object('next': json_query(dbo.chain(NULLIF(@x, 0))), 'at': @x ABSENT ON NULL); END\nGO\n",
            "SELECT json_query(dbo.chain(t.id)) AS v FROM dbo.t AS t FOR JSON PATH;");

        Assert.Empty(doc.Unknowns);
        Assert.True(doc.Types["ChainNode"].Recursive);
    }

    [Fact]
    public void An_unknown_inside_a_recursive_type_is_listed_under_every_procedure_that_reaches_it()
    {
        using var dir = new TempDirectory();
        dir.File("tables.sql", Tables);
        dir.File("functions.sql", """
            CREATE FUNCTION dbo.tree(@id int) RETURNS nvarchar(max) AS
            BEGIN
                RETURN (SELECT c.id, reverse(c.name) AS up, json_query(dbo.tree(c.id)) AS children FROM dbo.t AS c WHERE c.parent_id = @id FOR JSON PATH);
            END
            GO
            """);
        dir.File("p.sql", "CREATE PROCEDURE dbo.p AS\nSELECT json_query(dbo.tree(t.id)) AS v FROM dbo.t AS t FOR JSON PATH;");
        dir.File("q.sql", "CREATE PROCEDURE dbo.q AS\nSELECT json_query(dbo.tree(t.id)) AS w FROM dbo.t AS t FOR JSON PATH;");
        var project = SqlProject.Load(dir.Path);
        var doc = project.Infer(project.Select([ObjectPattern.Parse("*.*")]).Procedures);

        // The path starts at the type, which every procedure shares.
        Assert.Equal(["dbo.p <dbo.tree>.up", "dbo.q <dbo.tree>.up"], doc.Unknowns.Select(u => $"{u.Object} {u.Path}").Order(StringComparer.Ordinal));
    }

    // ---- the IR and the Zod ---------------------------------------------------------------------------

    [Fact]
    public void The_ir_writes_the_recursive_flag_and_json_text_content()
    {
        var json = IrJsonWriter.Write(Infer(Tree, "SELECT dbo.tree(t.id) AS text, json_query(dbo.tree(t.id)) AS live FROM dbo.t AS t FOR JSON PATH;"));

        Assert.Contains("\"TreeNode\": {\n      \"kind\": \"object\",\n      \"recursive\": true,", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\": \"json-text\",\n", json, StringComparison.Ordinal);
        Assert.Contains("\"content\": {", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Zod_writes_a_recursive_reference_as_a_getter()
    {
        var zod = ZodEmitter.Emit(Infer(Tree, "SELECT json_query(dbo.tree(t.id)) AS children FROM dbo.t AS t FOR JSON PATH;"));

        Assert.Contains("export const TreeNodeSchema = z.object({\n", zod, StringComparison.Ordinal);
        Assert.Contains("  get children() { return z.array(TreeNodeSchema).min(1).optional() },\n", zod, StringComparison.Ordinal);
        Assert.Contains("  id: z.int32(),\n", zod, StringComparison.Ordinal);
    }

    [Fact]
    public void Zod_checks_json_text_against_its_content_and_keeps_it_a_string()
    {
        var zod = ZodEmitter.Emit(Infer(Children, "SELECT dbo.children(t.id) AS kids FROM dbo.t AS t FOR JSON PATH;"));

        Assert.Contains("const jsonText = (content: () => z.ZodType) =>", zod, StringComparison.Ordinal);
        Assert.Contains("  kids: jsonText(() => z.array(PKidsSchema).min(1)).optional() /* JSON text */,\n", zod, StringComparison.Ordinal);
    }

    [Fact]
    public void Zod_writes_no_helper_when_no_json_text_has_content() =>
        Assert.DoesNotContain("jsonText", ZodEmitter.Emit(Infer(Children, "SELECT json_query(dbo.children(t.id)) AS kids FROM dbo.t AS t FOR JSON PATH;")), StringComparison.Ordinal);

    [Fact]
    public void Zod_writes_every_reference_within_a_cycle_as_a_getter()
    {
        var zod = ZodEmitter.Emit(Infer(Categories, "SELECT json_query(dbo.categories(t.id)) AS tree FROM dbo.t AS t FOR JSON PATH;"));

        // Whichever is written first, each refers to the other inside the cycle, so both are getters; the row, outside
        // the cycle, refers to it plainly.
        Assert.Contains("  get subcategories() { return z.array(CategoriesNodeSchema).min(1).optional() },\n", zod, StringComparison.Ordinal);
        Assert.Contains("  get items() { return ItemsNodeSchema },\n", zod, StringComparison.Ordinal);
        Assert.Contains("  tree: z.array(CategoriesNodeSchema).min(1).optional(),\n", zod, StringComparison.Ordinal);
    }
}
