using Haruspex.Cli;
using Haruspex.Ir;

namespace Haruspex.Tests.Inference;

public partial class JsonFunctionTests
{
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
        Assert.Equal([IrNullRule.EmptySubquery], Rules(inner.NullableBecause));
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
        Assert.Equal([IrNullRule.EmptySubquery], Rules(sub.NullableBecause));
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

        Assert.Equal(IrUnknownReason.UnresolvedRecursion, Assert.Single(doc.Unknowns, u => u.Path == "results[0][].v").Reason);
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

        Assert.Equal(IrUnknownReason.UnresolvedRecursion, Assert.Single(doc.Unknowns, u => u.Path == "results[0][].v").Reason);
        Assert.DoesNotContain("ForeverNode", doc.Types.Keys);
    }

    [Fact]
    public void A_recursive_type_with_no_finite_value_through_a_plain_call_is_unknown()
    {
        // The call is text, but text holding a value that never ends: the server raises error 217 every time.
        var doc = Infer(
            "CREATE FUNCTION dbo.forever_text(@x int) RETURNS nvarchar(max) AS BEGIN RETURN json_object('c': dbo.forever_text(@x)); END\nGO\n",
            "SELECT json_query(dbo.forever_text(t.id)) AS v, dbo.forever_text(t.id) AS w FROM dbo.t AS t FOR JSON PATH;");

        Assert.Equal(IrUnknownReason.UnresolvedRecursion, Assert.Single(doc.Unknowns, u => u.Path == "results[0][].v").Reason);
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
                RETURN (SELECT c.id, checksum(c.name) AS up, json_query(dbo.tree(c.id)) AS children FROM dbo.t AS c WHERE c.parent_id = @id FOR JSON PATH);
            END
            GO
            """);
        dir.File("p.sql", "CREATE PROCEDURE dbo.p AS\nSELECT json_query(dbo.tree(t.id)) AS v FROM dbo.t AS t FOR JSON PATH;");
        dir.File("q.sql", "CREATE PROCEDURE dbo.q AS\nSELECT json_query(dbo.tree(t.id)) AS w FROM dbo.t AS t FOR JSON PATH;");
        var project = SqlProject.Load(dir.Path);
        var doc = project.Infer(project.Select([ObjectPattern.Parse("*.*")]).Procedures).Expected();

        // The path starts at the type, which every procedure shares.
        Assert.Equal(["dbo.p <dbo.tree>.up", "dbo.q <dbo.tree>.up"], doc.Unknowns.Select(u => $"{u.Object} {u.Path}").Order(StringComparer.Ordinal));
    }
}
