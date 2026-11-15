using Haruspex.Ir;

namespace Haruspex.Tests.Inference;

/// <summary>
/// Facts from the schema travel to the output only while a value passes through unchanged: a column reference,
/// through aliases, joins and FOR JSON nesting. Anything else drops them, which leaves the type wider and still true.
/// </summary>
public class FactFlowTests
{
    private const string Tables = """
        CREATE TYPE dbo.code FROM nvarchar(8) NOT NULL;
        GO
        CREATE TABLE dbo.t (
            id int NOT NULL PRIMARY KEY,
            status varchar(10) COLLATE Latin1_General_100_CI_AS NOT NULL CONSTRAINT ck_t_status CHECK (status IN ('open', 'closed')),
            score smallint NULL CONSTRAINT ck_t_score CHECK (score BETWEEN 1 AND 5),
            code dbo.code);
        GO
        CREATE TABLE dbo.u (id int NOT NULL PRIMARY KEY, t_id int NOT NULL, title nvarchar(20) NOT NULL CHECK (len(title) > 0));
        GO
        """;

    private static IrDocument Procedure(string body) =>
        InferProcedure.Infer("*.*", ("tables.sql", Tables), ("p.sql", $"CREATE PROCEDURE dbo.p @code dbo.code, @name sysname AS\n{body}"));

    private static IrField Field(IrDocument doc, string type, string name) => doc.Types[type].Fields.Single(f => f.Name == name);

    private static IrFacets? Facets(IrField field) => Assert.IsType<IrScalar>(field.Type).Facets;

    [Fact]
    public void A_column_keeps_its_facts_under_an_alias_and_in_a_rowset()
    {
        var doc = Procedure("SELECT t.status AS state, t.score AS s, t.code FROM dbo.t AS t;");

        var state = Facts(doc, "PRow", "state");
        Assert.Equal(["closed", "open"], state.Values!.Items.Select(i => i.Text));
        Assert.Equal((IrValueMatch.Canonical, "Latin1_General_100_CI_AS.codepage"), (state.Values.Match, state.Values.Fold));
        Assert.Equal(["Latin1_General_100_CI_AS.codepage"], doc.Folds.Keys);
        Assert.Equal("dbo.ck_t_status", Assert.Single(state.Values.From).Ref);
        Assert.Equal((10, IrLengthUnit.Bytes, IrByteEncoding.SingleByte), (state.Length!.Value, state.Length.Unit, state.Length.Encoding));

        var s = Facts(doc, "PRow", "s");
        Assert.Equal(("1", "5"), (s.Min!.Value, s.Max!.Value));

        // An alias type: its facets, attributed to it, and its NOT NULL.
        var code = Field(doc, "PRow", "code");
        Assert.False(code.Nullable);
        Assert.Equal(new IrFactSource(IrFactSourceKind.Type, "dbo.code"), Facets(code)!.Length!.From);
        Assert.Equal("nvarchar(8)", Assert.IsType<IrScalar>(code.Type).Sql);
    }

    private static IrFacets Facts(IrDocument doc, string type, string name) => Facets(Field(doc, type, name))!;

    [Fact]
    public void A_column_keeps_its_facts_through_a_join_and_a_nested_for_json_subquery()
    {
        var doc = Procedure("""
            SELECT t.score AS [detail.score],
                   u.title AS firstTitle,
                   (SELECT u2.title FROM dbo.u AS u2 WHERE u2.t_id = t.id FOR JSON PATH) AS titles
            FROM dbo.t AS t
            LEFT JOIN dbo.u AS u ON u.t_id = t.id
            FOR JSON PATH;
            """);

        Assert.Equal("5", Facts(doc, "PDetail", "score").Max!.Value);

        // The outer join makes the title nullable; what a title is when present doesn't change.
        var first = Field(doc, "PRow", "firstTitle");
        Assert.True(first.Nullable);
        Assert.Equal(1, Facets(first)!.MinLength!.Value);
        Assert.Equal(1, Facts(doc, "PTitle", "title").MinLength!.Value);
    }

    [Fact]
    public void An_expression_not_modelled_drops_the_facts_with_the_type() =>
        Assert.IsType<IrUnknownType>(Field(Procedure("SELECT hashbytes('SHA2_256', t.status) AS v FROM dbo.t AS t;"), "PRow", "v").Type);

    [Theory]
    [InlineData("SELECT t.score + 1 AS v FROM dbo.t AS t;", "-2147483648")]
    [InlineData("SELECT CAST(t.score AS int) AS v FROM dbo.t AS t;", "-2147483648")]
    [InlineData("SELECT ISNULL(t.score, 0) AS v FROM dbo.t AS t;", "-32768")]
    public void An_expression_keeps_only_its_types_facts(string body, string min)
    {
        // A value computed from a column is any value of the expression's type: the column's CHECK does not hold.
        var facets = Facets(Field(Procedure(body), "PRow", "v"))!;
        Assert.Equal(min, facets.Min!.Value);
        Assert.Null(facets.Values);
    }

    [Fact]
    public void A_cast_to_the_columns_own_type_keeps_its_facts() =>
        Assert.Equal("5", Facets(Field(Procedure("SELECT CAST(t.score AS smallint) AS v FROM dbo.t AS t;"), "PRow", "v"))!.Max!.Value);

    [Fact]
    public void Json_text_carries_no_facts() =>
        Assert.IsType<IrJsonText>(Field(Procedure("SELECT (SELECT t.score FROM dbo.t AS t FOR JSON PATH) AS j;"), "PRow", "j").Type);

    [Fact]
    public void A_parameter_of_an_alias_type_has_its_kind_but_no_facets()
    {
        var parameters = Procedure("SELECT t.id FROM dbo.t AS t;").Objects[0].Parameters;

        Assert.All(parameters, p => Assert.Null(Assert.IsType<IrScalar>(p.Type).Facets));
        Assert.Equal(["nvarchar(8)", "nvarchar(128)"], parameters.Select(p => ((IrScalar)p.Type).Sql));
    }
}
