using Haruspex.Ir;

namespace Haruspex.Tests.Inference;

public partial class JsonFunctionTests
{
    [Fact]
    public void A_plain_call_of_a_for_json_function_is_text_whose_content_is_known()
    {
        var doc = Infer(Children, "SELECT t.id, dbo.children(t.id) AS kids FROM dbo.t AS t FOR JSON PATH;");
        var kids = Field(doc, "kids");

        // Observed: [{"x":"[{\"id\":2,…}]"}]. Escaped, so a string; NULL over no rows, so the key is left out.
        var text = Assert.IsType<IrJsonText>(kids.Type);
        var items = Assert.IsType<IrArray>(text.Content);
        Assert.Equal(["id", "name"], Fields(doc, items.Items).Select(f => f.Name));
        Assert.Equal([IrNullRule.EmptySubquery], Rules(kids.NullableBecause));
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
        Assert.Equal(IrFormat.Int32, Assert.IsType<IrScalar>(id.Type).Format);
        Assert.Equal([IrNullRule.EmptySubquery], Rules(kids.NullableBecause));
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
        Assert.Equal([IrNullRule.Parameter], Rules(id.NullableBecause));
        Assert.Equal(IrPresence.Always, id.Presence);
        Assert.Equal(IrFormat.Int32, Assert.IsType<IrScalar>(id.Type).Format);

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
        Assert.Equal([IrNullRule.EmptySubquery], Rules(v.NullableBecause));
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
        Assert.Equal([IrNullRule.FunctionResult], Rules(v.NullableBecause));
        Assert.Equal(IrUnknownReason.JsonShapeUnknown, Assert.IsType<IrUnknownType>(Field(doc, "q").Type).Reason);
    }

    [Fact]
    public void A_function_of_another_type_gives_its_declared_type_and_may_be_null()
    {
        var doc = Infer(
            "CREATE FUNCTION dbo.twice(@n int) RETURNS int AS BEGIN RETURN @n * 2; END\nGO\n",
            "SELECT dbo.twice(t.id) AS v FROM dbo.t AS t FOR JSON PATH;");
        var v = Field(doc, "v");

        Assert.Equal(IrFormat.Int32, Assert.IsType<IrScalar>(v.Type).Format);
        Assert.Equal([IrNullRule.FunctionResult], Rules(v.NullableBecause));
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
        Assert.Equal([IrNullRule.NullInput], Rules(maybe.NullableBecause));
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
        // In another script than its CREATE, so which definition applies depends on the order the scripts run.
        var doc = InferProcedure.Infer(
            "dbo.p",
            ("tables.sql", Tables),
            ("functions.sql", Children),
            ("altered.sql", "ALTER FUNCTION dbo.children(@id int) RETURNS int AS BEGIN RETURN 1; END"),
            ("p.sql", "CREATE PROCEDURE dbo.p AS\nSELECT dbo.children(t.id) AS v FROM dbo.t AS t FOR JSON PATH;"));

        Assert.Equal(IrUnknownReason.Redefined, Assert.Single(doc.Unknowns, u => u.Path == "results[0][].v").Reason);
    }
}
