using Haruspex.Ir;
using Haruspex.SqlServer.Inference;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

/// <summary>
/// XML values (decision 0133): the types that hold them, their conversions, and their methods, each a scalar of kind <c>xml</c>
/// with no facets, or what a method gives. Each type and reason to be null rests on an observation in the differential tests'
/// <c>XmlObservationTests</c>, named where it matters.
/// </summary>
public class XmlTests
{
    private const string Tables = """
        CREATE TABLE shop.product (
            product_id int NOT NULL PRIMARY KEY, name nvarchar(40) NOT NULL, spec xml NOT NULL, notes xml NULL);
        GO
        """;

    private static IrDocument Procedure(string body) =>
        InferProcedure.Infer("shop.p", ("tables.sql", Tables), ("p.sql", $"CREATE PROCEDURE shop.p AS\n{body}"));

    private static IrDocument Procedure(string parameters, string body) =>
        InferProcedure.Infer("shop.p", ("tables.sql", Tables), ("p.sql", $"CREATE PROCEDURE shop.p {parameters} AS\n{body}"));

    /// <summary>The field's kind and SQL type (<c>xml xml</c>), or <c>unknown</c> with the reason's detail.</summary>
    private static string Typed(IrDocument doc, string name) => Field(doc, name).Type switch
    {
        IrScalar scalar => $"{IrWireNames.Of(scalar.Kind)} {scalar.Sql}",
        IrUnknownType => "unknown: " + string.Join("; ", doc.Unknowns.Where(u => u.Path.EndsWith("." + name, StringComparison.Ordinal)).Select(u => u.Detail)),
        var other => other.ToString(),
    };

    // ---- the type --------------------------------------------------------------------------------------------------------

    [Fact]
    public void An_xml_column_is_xml_with_no_facets_null_only_where_it_may_be()
    {
        // An_xml_column_is_null_only_where_it_is_and_an_empty_instance_is_not.
        var doc = Procedure("SELECT p.spec, p.notes FROM shop.product AS p;");

        Assert.Equal("xml xml", Typed(doc, "spec"));
        Assert.Equal("xml xml", Typed(doc, "notes"));
        Assert.Null(Assert.IsType<IrScalar>(Field(doc, "spec").Type).Facets);
        Assert.Empty(Field(doc, "spec").NullableBecause);
        Assert.Equal([IrNullRule.NullableColumn], Rules(doc, "notes"));
        Assert.Equal(IrDocument.CurrentVersion, doc.Version);
    }

    [Fact]
    public void An_xml_parameter_and_variable_are_xml()
    {
        var doc = Procedure("@draft xml", "DECLARE @copy xml = @draft; SELECT @draft AS draft, @copy AS copy;");

        Assert.Equal(IrScalarKind.Xml, Assert.IsType<IrScalar>(Assert.Single(doc.Objects[0].Parameters).Type).Kind);
        Assert.Equal("xml xml", Typed(doc, "draft"));
        Assert.Equal("xml xml", Typed(doc, "copy"));
    }

    // ---- conversions -----------------------------------------------------------------------------------------------------

    [Fact]
    public void A_conversion_to_or_from_xml_is_null_only_when_its_operand_is()
    {
        // A_conversion_to_xml_is_null_only_for_null_and_try_forms_also_for_text_that_is_not_xml, and
        // A_conversion_to_or_from_xml_fails_rather_than_giving_null_even_under_the_callers_options: no overflow reason.
        var doc = Procedure("""
            SELECT CAST(p.name AS xml) AS a, CONVERT(xml, N'<a/>', 1) AS b, CAST(p.spec AS nvarchar(10)) AS c,
                   CAST(p.notes AS nvarchar(max)) AS d
            FROM shop.product AS p;
            """);

        Assert.Equal("xml xml", Typed(doc, "a"));
        Assert.Empty(Field(doc, "a").NullableBecause);
        Assert.Empty(Field(doc, "b").NullableBecause);
        Assert.Equal("string nvarchar(10)", Typed(doc, "c"));
        Assert.Empty(Field(doc, "c").NullableBecause);
        Assert.Equal([IrNullRule.NullableColumn], Rules(doc, "d"));
    }

    [Fact]
    public void Try_cast_and_try_convert_to_or_from_xml_may_be_null()
    {
        var doc = Procedure("SELECT TRY_CAST(p.name AS xml) AS a, TRY_CONVERT(nvarchar(3), p.spec) AS b FROM shop.product AS p;");

        Assert.Equal([IrNullRule.FailedConversion], Rules(doc, "a"));
        Assert.Equal([IrNullRule.FailedConversion], Rules(doc, "b"));
    }

    [Fact]
    public void Text_beside_xml_is_xml_with_no_facets()
    {
        // Text_beside_xml_takes_xml_by_precedence: a literal's value set and length don't survive being parsed as XML.
        var doc = Procedure("""
            SELECT CASE WHEN p.product_id = 1 THEN p.spec ELSE N'<none/>' END AS a, ISNULL(p.notes, N'<none/>') AS b,
                   COALESCE(p.notes, p.spec) AS c
            FROM shop.product AS p;
            """);

        Assert.Equal("xml xml", Typed(doc, "a"));
        Assert.Null(Assert.IsType<IrScalar>(Field(doc, "a").Type).Facets);
        Assert.Empty(Field(doc, "b").NullableBecause);
        Assert.Empty(Field(doc, "c").NullableBecause);
    }

    [Fact]
    public void What_compares_xml_is_unknown()
    {
        // Xml_can_t_be_compared_so_min_max_nullif_and_string_agg_of_it_are_refused.
        var doc = Procedure("SELECT MAX(p.spec) AS a, NULLIF(p.spec, p.spec) AS b, STRING_AGG(p.spec, ',') AS c FROM shop.product AS p;");

        Assert.StartsWith("unknown: ", Typed(doc, "a"), StringComparison.Ordinal);
        Assert.StartsWith("unknown: ", Typed(doc, "b"), StringComparison.Ordinal);
        Assert.StartsWith("unknown: ", Typed(doc, "c"), StringComparison.Ordinal);
    }

    // ---- methods ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void Value_is_its_type_argument_s_type_and_always_nullable()
    {
        // Xml_value_is_its_type_arguments_type_and_null_where_the_path_finds_no_node: over a column never null too.
        var doc = Procedure("""
            SELECT p.spec.value('(/spec/@weight)[1]', 'decimal(9,2)') AS weight, p.spec.value('(/spec/@code)[1]', N'nvarchar') AS code
            FROM shop.product AS p;
            """);

        Assert.Equal("number decimal(9,2)", Typed(doc, "weight"));
        Assert.Equal([IrNullRule.XmlValue], Rules(doc, "weight"));
        Assert.Equal("string nvarchar(1)", Typed(doc, "code"));
        Assert.Equal(1, Assert.IsType<IrScalar>(Field(doc, "code").Type).Facets?.Length?.Value);
        Assert.Equal([IrNullRule.XmlValue], Rules(doc, "code"));
    }

    [Fact]
    public void Value_of_a_variable_or_an_expression_is_typed_too()
    {
        var doc = Procedure("@draft xml", "SELECT @draft.value('(/a)[1]', 'int') AS a, CAST(N'<a>1</a>' AS xml).value('(/a)[1]', 'bigint') AS b;");

        Assert.Equal("number int", Typed(doc, "a"));
        Assert.Equal("number bigint", Typed(doc, "b"));
        Assert.Equal([IrNullRule.XmlValue], Rules(doc, "b"));
    }

    [Fact]
    public void A_column_named_alone_before_a_method_is_the_column()
    {
        // A_method_of_a_column_named_as_a_schema_with_a_function_of_that_name_is_refused_as_ambiguous: spec.value is the column's.
        var doc = Procedure("SELECT spec.value('(/a)[1]', 'int') AS a FROM shop.product;");

        Assert.Equal("number int", Typed(doc, "a"));
    }

    [Theory]
    [InlineData("int", "int")]
    [InlineData(" INT ", "int")]
    [InlineData("[int]", "int")]
    [InlineData("sys.int", "int")]
    [InlineData("SYS.Int", "int")]
    [InlineData("decimal", "decimal(18,0)")]
    [InlineData("decimal ( 9 , 2 )", "decimal(9,2)")]
    [InlineData("nvarchar", "nvarchar(1)")]
    [InlineData("varchar(max)", "varchar(max)")]
    [InlineData("datetime2", "datetime2(7)")]
    [InlineData("uniqueidentifier", "uniqueidentifier")]
    [InlineData("xml", null)]
    [InlineData("json", null)]
    [InlineData("vector(3)", null)]
    [InlineData("sql_variant", null)]
    [InlineData("text", null)]
    [InlineData("hierarchyid", null)]
    [InlineData("dbo.code", null)]
    [InlineData("sysname", null)]
    [InlineData("int)", null)]
    public void Value_s_type_argument_is_read_as_a_declaration_or_not_at_all(string written, string? type) =>
        // Xml_value_is_its_type_arguments_type_and_null_where_the_path_finds_no_node, and
        // Xml_value_of_json_or_vector_is_typed_by_what_the_client_supports_and_other_types_are_refused.
        Assert.Equal(type, Typer.XmlValueType(written)?.Text);

    [Fact]
    public void Value_with_an_argument_that_is_no_literal_is_unknown()
    {
        // Xml_methods_take_only_literal_arguments: the server refuses the procedure.
        var doc = Procedure("DECLARE @t nvarchar(10) = N'int'; SELECT p.spec.value('(/a)[1]', @t) AS a FROM shop.product AS p;");

        Assert.StartsWith("unknown: ", Typed(doc, "a"), StringComparison.Ordinal);
    }

    [Fact]
    public void Exist_is_a_bit_null_only_where_the_xml_may_be()
    {
        // Xml_exist_is_a_bit_null_exactly_when_the_xml_is.
        var doc = Procedure("SELECT p.spec.exist('/spec') AS a, p.notes.exist('/notes') AS b FROM shop.product AS p;");

        Assert.Equal("boolean bit", Typed(doc, "a"));
        Assert.Empty(Field(doc, "a").NullableBecause);
        Assert.Equal([IrNullRule.NullableColumn], Rules(doc, "b"));
    }

    [Fact]
    public void Query_and_nodes_are_unknown_with_a_reason()
    {
        var query = Procedure("SELECT p.spec.query('/spec') AS a FROM shop.product AS p;");
        var nodes = Procedure("SELECT n.c.value('.', 'int') AS a FROM shop.product AS p CROSS APPLY p.spec.nodes('/spec/part') AS n(c);");

        Assert.Contains("XML's query() is not modelled yet", Typed(query, "a"), StringComparison.Ordinal);
        Assert.Equal(IrUnknownReason.UnsupportedExpression, Assert.IsType<IrUnknownType>(Field(query, "a").Type).Reason);
        Assert.Contains("XML's nodes()", Typed(nodes, "a"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_method_of_a_value_that_is_not_xml_is_unknown()
    {
        var doc = Procedure("SELECT CAST(p.name AS nvarchar(40)).value('(/a)[1]', 'int') AS a FROM shop.product AS p;");

        Assert.StartsWith("unknown: ", Typed(doc, "a"), StringComparison.Ordinal);
    }

    // ---- schema collections ----------------------------------------------------------------------------------------------

    private const string Collections = """
        CREATE XML SCHEMA COLLECTION shop.spec_schema AS N'<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"><xs:element name="spec"/></xs:schema>';
        GO
        CREATE XML SCHEMA COLLECTION dbo.card_schema AS N'<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"><xs:element name="card"/></xs:schema>';
        GO
        CREATE XML SCHEMA COLLECTION shop.card_schema AS N'<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"><xs:element name="shop_card"/></xs:schema>';
        GO
        CREATE TABLE shop.typed (id int NOT NULL PRIMARY KEY, spec xml(shop.spec_schema) NOT NULL, card xml(DOCUMENT dbo.card_schema) NULL, plain xml NULL);
        GO
        """;

    private static IrDocument Typed(string parameters, string body) =>
        InferProcedure.Infer("shop.p", ("tables.sql", Tables + "\n" + Collections), ("p.sql", $"CREATE PROCEDURE shop.p {parameters} AS\n{body}"));

    [Fact]
    public void Each_typed_column_is_spelt_with_the_collection_it_is_bound_to()
    {
        // An_unqualified_xml_schema_collection_is_found_as_an_alias_type_is.
        var doc = Typed("", "SELECT t.spec, t.card, t.plain FROM shop.typed AS t;");

        Assert.Equal("xml xml(shop.spec_schema)", Typed(doc, "spec"));
        Assert.Equal("xml xml(DOCUMENT dbo.card_schema)", Typed(doc, "card"));
        Assert.Equal("xml xml", Typed(doc, "plain"));
        Assert.Empty(Field(doc, "spec").NullableBecause);
    }

    [Fact]
    public void A_name_without_a_schema_is_found_in_the_module_s_schema_then_dbo()
    {
        // In shop.p, card_schema is shop's though dbo has one too, and a name only dbo has is dbo's.
        var doc = Typed("@given xml(card_schema) = NULL", """
            DECLARE @spec xml(spec_schema) = N'<spec/>';
            SELECT @given AS given, @spec AS spec;
            """);

        Assert.Equal("xml(shop.card_schema)", Assert.IsType<IrScalar>(Assert.Single(doc.Objects[0].Parameters).Type).Sql);
        Assert.Equal("xml xml(shop.card_schema)", Typed(doc, "given"));
        Assert.Equal("xml xml(shop.spec_schema)", Typed(doc, "spec"));
        Assert.Empty(Field(doc, "spec").NullableBecause);
    }

    [Fact]
    public void A_name_written_without_a_schema_outside_a_module_and_found_twice_is_spelt_as_written()
    {
        // A table's column takes its creator's default schema, which a folder doesn't say: card_schema is in dbo and in shop.
        var doc = InferProcedure.Infer(
            "shop.p",
            ("tables.sql", Collections + "\nCREATE TABLE shop.loose (id int NOT NULL PRIMARY KEY, card xml(card_schema) NULL, spec xml(spec_schema) NULL);\nGO\n"),
            ("p.sql", "CREATE PROCEDURE shop.p AS SELECT l.card, l.spec FROM shop.loose AS l;"));

        Assert.Equal("xml xml(card_schema)", Typed(doc, "card"));
        Assert.Equal("xml xml(shop.spec_schema)", Typed(doc, "spec"));
    }

    [Fact]
    public void Value_of_a_typed_column_is_typed_by_its_type_argument_alone()
    {
        // The collection isn't read yet: what value() gives is its type argument's, nullable, as for untyped xml.
        var doc = Typed("", "SELECT t.spec.value('(/spec/@weight)[1]', 'int') AS weight FROM shop.typed AS t;");

        Assert.Equal("number int", Typed(doc, "weight"));
        Assert.Equal([IrNullRule.XmlValue], Rules(doc, "weight"));
    }
}
