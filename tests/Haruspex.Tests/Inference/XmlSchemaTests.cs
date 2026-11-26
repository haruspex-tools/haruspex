using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

/// <summary>
/// XML typed from its schema collection (decision 0139): a node the schemas require is there in a typed instance, so its
/// <c>value()</c> is NULL only where the instance is, and <c>nodes()</c> gives as many rows as they allow. Each rule rests on an
/// observation in the differential tests' <c>XmlSchemaObservationTests</c>, named where it matters.
/// </summary>
public class XmlSchemaTests
{
    /// <summary>An order requires its customer, a line or more, and an id; its note, price and qty may be missing or nil.</summary>
    private const string OrderSchema = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
         <xs:element name="order">
          <xs:complexType>
           <xs:sequence>
            <xs:element name="customer" type="xs:string"/>
            <xs:element name="note" type="xs:string" minOccurs="0"/>
            <xs:element name="qty" nillable="true" type="xs:int"/>
            <xs:element name="line" maxOccurs="3">
             <xs:complexType>
              <xs:sequence><xs:element name="part" type="xs:string" maxOccurs="unbounded"/></xs:sequence>
              <xs:attribute name="sku" type="xs:string" use="required"/>
              <xs:attribute name="gift" type="xs:boolean"/>
             </xs:complexType>
            </xs:element>
            <xs:element name="box" minOccurs="0"><xs:complexType><xs:attribute name="size" type="xs:int" use="required"/></xs:complexType></xs:element>
           </xs:sequence>
           <xs:attribute name="id" type="xs:int" use="required"/>
           <xs:attribute name="channel" type="xs:string" default="web"/>
           <xs:attribute name="price" type="xs:decimal"/>
          </xs:complexType>
         </xs:element>
        </xs:schema>
        """;

    private const string Schema = $"""
        CREATE XML SCHEMA COLLECTION shop.order_schema AS N'{OrderSchema}';
        GO
        CREATE TABLE shop.orders (
            order_id int NOT NULL PRIMARY KEY, doc xml(DOCUMENT shop.order_schema) NOT NULL,
            maybe xml(DOCUMENT shop.order_schema) NULL, loose xml(shop.order_schema) NOT NULL, plain xml NOT NULL);
        GO
        """;

    private static IrDocument Procedure(string body, string schema = "") =>
        InferProcedure.Infer("shop.p", ("tables.sql", schema.Length > 0 ? schema : Schema), ("p.sql", $"CREATE PROCEDURE shop.p AS\n{body}"));

    private static IrDocument WithParameters(string parameters, string body) =>
        InferProcedure.Infer("shop.p", ("tables.sql", Schema), ("p.sql", $"CREATE PROCEDURE shop.p {parameters} AS\n{body}"));

    /// <summary><c>shop.p</c> selecting one value of the column <c>doc</c>, read by <paramref name="path"/> as <paramref name="type"/>.</summary>
    private static IrDocument Value(string path, string type = "nvarchar(20)", string column = "doc") =>
        Procedure($"SELECT o.{column}.value('{path}', '{type}') AS v FROM shop.orders AS o;");

    // ---- value() ---------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("(/order/@id)[1]")]
    [InlineData("/order/@id")]
    [InlineData("(/order/customer)[1]")]
    [InlineData("/order[1]/customer[1]")]
    [InlineData("(/order/line/@sku)[1]")]
    [InlineData("(/order/line/part)[1]")]
    [InlineData("  (  /order / @id ) [ 1 ]  ")]
    public void A_required_node_s_value_is_never_null_in_a_document_instance(string path) =>
        // A_required_node_s_value_is_never_null_in_a_typed_instance.
        Assert.Empty(Field(Value(path), "v").NullableBecause);

    [Fact]
    public void A_required_node_s_value_is_null_only_where_its_instance_is() => Assert.Equal([IrNullRule.NullableColumn], Rules(Value("(/order/@id)[1]", "int", "maybe"), "v"));

    [Theory]
    [InlineData("(/order/note)[1]")]
    [InlineData("(/order/@price)[1]")]
    [InlineData("(/order/box/@size)[1]")]
    [InlineData("(/order/line/@gift)[1]")]
    public void An_optional_node_s_value_may_be_null(string path) =>
        Assert.Equal([IrNullRule.XmlValue], Rules(Value(path), "v"));

    [Fact]
    public void A_nillable_element_s_value_may_be_null_though_it_is_required() =>
        // A_nil_element_s_value_is_null.
        Assert.Equal([IrNullRule.XmlValue], Rules(Value("(/order/qty)[1]", "int"), "v"));

    [Fact]
    public void An_attribute_with_a_default_is_always_there() =>
        // A_default_or_fixed_attribute_is_written_into_the_instance.
        Assert.Empty(Field(Value("(/order/@channel)[1]"), "v").NullableBecause);

    [Theory]
    [InlineData("loose")]
    [InlineData("plain")]
    public void A_content_or_untyped_instance_proves_nothing_from_its_top_level(string column) =>
        // A_content_instance_may_be_empty_or_have_several_top_level_elements.
        Assert.Equal([IrNullRule.XmlValue], Rules(Value("(/order/@id)[1]", "int", column), "v"));

    [Fact]
    public void A_typed_variable_and_parameter_are_read_by_their_collection()
    {
        var doc = WithParameters("@given xml(DOCUMENT shop.order_schema)", """
            DECLARE @copy xml(DOCUMENT shop.order_schema) = @given;
            SELECT @given.value('(/order/@id)[1]', 'int') AS given, @copy.value('(/order/@id)[1]', 'int') AS copy;
            """);

        Assert.Equal([IrNullRule.Parameter], Rules(doc, "given"));
        Assert.DoesNotContain(IrNullRule.XmlValue, Rules(doc, "copy"));
    }

    [Theory]
    [InlineData("(/order/@*)[1]")]
    [InlineData("(//customer)[1]")]
    [InlineData("(/order/customer/text())[1]")]
    [InlineData("string((/order/customer)[1])")]
    [InlineData("(/order/line[2]/@sku)[1]")]
    [InlineData("(/order/line/@sku)[1] (: a comment :)")]
    [InlineData("(/x:order/@id)[1]")]
    public void A_path_not_read_proves_nothing(string path) =>
        Assert.Equal([IrNullRule.XmlValue], Rules(Value(path), "v"));

    [Fact]
    public void A_batch_with_xml_namespaces_proves_nothing()
    {
        // A default element namespace would move /order out of the schema's namespace, which the path doesn't show. The statement
        // that declares it is unknown today, so the batch is what is asked of.
        var doc = Procedure("""
            WITH XMLNAMESPACES (DEFAULT 'urn:elsewhere') SELECT 1 AS one;
            SELECT o.doc.value('(/order/@id)[1]', 'int') AS v FROM shop.orders AS o;
            """);

        Assert.Equal([IrNullRule.XmlValue], Field(doc, "v", 1).NullableBecause.Select(r => r.Rule));
    }

    // ---- what the collection holds ---------------------------------------------------------------------------------------

    [Fact]
    public void A_second_global_element_may_be_a_document_s_root()
    {
        // A_document_s_root_is_any_global_element_of_any_schema_in_the_collection: ALTER … ADD may add one.
        var doc = Procedure("SELECT o.doc.value('(/order/@id)[1]', 'int') AS v FROM shop.orders AS o;", Schema + "\n" + """
            ALTER XML SCHEMA COLLECTION shop.order_schema ADD N'<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="urn:returns"><xs:element name="return"/></xs:schema>';
            GO
            """);

        Assert.Equal([IrNullRule.XmlValue], Rules(doc, "v"));
    }

    private static IrDocument Shaped(string schema, string path)
    {
        var tables = $"""
            CREATE XML SCHEMA COLLECTION shop.s AS N'<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">{schema}</xs:schema>';
            GO
            CREATE TABLE shop.t (id int NOT NULL PRIMARY KEY, doc xml(DOCUMENT shop.s) NOT NULL);
            GO
            """;
        return Procedure($"SELECT t.doc.value('{path}', 'int') AS v FROM shop.t AS t;", tables);
    }

    [Fact]
    public void A_path_through_an_element_whose_type_another_derives_from_proves_nothing()
    {
        // An_instance_may_name_a_derived_type_in_xsi_type.
        var doc = Shaped("""
            <xs:complexType name="base"><xs:sequence><xs:element name="p" type="xs:int"/></xs:sequence></xs:complexType>
            <xs:complexType name="more"><xs:complexContent><xs:extension base="base"><xs:sequence><xs:element name="q" type="xs:int"/></xs:sequence></xs:extension></xs:complexContent></xs:complexType>
            <xs:element name="r"><xs:complexType><xs:sequence><xs:element name="c" type="base"/></xs:sequence></xs:complexType></xs:element>
            """, "(/r/c/p)[1]");

        Assert.Equal([IrNullRule.XmlValue], Rules(doc, "v"));
    }

    [Fact]
    public void A_simple_type_another_derives_from_still_proves_its_node_is_there()
    {
        // A derived simple type only narrows the values, so xsi:type can't take the node away.
        var doc = Shaped("""
            <xs:simpleType name="small"><xs:restriction base="xs:int"><xs:maxInclusive value="9"/></xs:restriction></xs:simpleType>
            <xs:element name="r"><xs:complexType><xs:sequence><xs:element name="c" type="xs:int"/></xs:sequence></xs:complexType></xs:element>
            """, "(/r/c)[1]");

        Assert.Empty(Field(doc, "v").NullableBecause);
    }

    [Theory]
    [InlineData("(/r/batch/k)[1]", false)]
    [InlineData("(/r/batch/@id)[1]", true)]
    public void A_nillable_element_s_children_may_be_missing_though_its_attributes_are_there(string path, bool there)
    {
        // A_nil_element_has_no_children_but_keeps_its_attributes.
        var doc = Shaped("""
            <xs:element name="r"><xs:complexType><xs:sequence>
             <xs:element name="batch" nillable="true"><xs:complexType><xs:sequence><xs:element name="k" type="xs:int"/></xs:sequence><xs:attribute name="id" type="xs:int" use="required"/></xs:complexType></xs:element>
            </xs:sequence></xs:complexType></xs:element>
            """, path);

        Assert.Equal(there ? [] : [IrNullRule.XmlValue], Rules(doc, "v"));
    }

    [Fact]
    public void A_path_through_a_substitution_group_s_head_proves_nothing()
    {
        // A_substitution_group_s_member_stands_where_its_head_is_declared.
        var doc = Shaped("""
            <xs:element name="head" type="xs:int"/>
            <xs:element name="member" type="xs:int" substitutionGroup="head"/>
            <xs:element name="r"><xs:complexType><xs:sequence><xs:element ref="head"/></xs:sequence></xs:complexType></xs:element>
            """, "(/r/head)[1]");

        Assert.Equal([IrNullRule.XmlValue], Rules(doc, "v"));
    }

    [Fact]
    public void A_path_through_content_with_a_wildcard_proves_nothing()
    {
        // A_wildcard_may_take_an_element_of_a_declared_name_before_it.
        var doc = Shaped("""
            <xs:element name="r"><xs:complexType><xs:sequence><xs:any processContents="skip"/><xs:element name="b" type="xs:int"/></xs:sequence></xs:complexType></xs:element>
            """, "(/r/b)[1]");

        Assert.Equal([IrNullRule.XmlValue], Rules(doc, "v"));
    }

    [Fact]
    public void A_declared_attribute_beside_an_attribute_wildcard_is_still_proved()
    {
        // A_declared_attribute_is_validated_beside_an_attribute_wildcard.
        var doc = Shaped("""
            <xs:element name="r"><xs:complexType><xs:attribute name="id" type="xs:int" use="required"/><xs:anyAttribute processContents="skip"/></xs:complexType></xs:element>
            """, "(/r/@id)[1]");

        Assert.Empty(Field(doc, "v").NullableBecause);
    }

    [Fact]
    public void A_choice_requires_only_what_each_branch_has()
    {
        var doc = Shaped("""
            <xs:element name="r"><xs:complexType><xs:choice><xs:sequence><xs:element name="a" type="xs:int"/><xs:element name="b" type="xs:int"/></xs:sequence><xs:sequence><xs:element name="c" type="xs:int"/><xs:element name="a" type="xs:int"/></xs:sequence></xs:choice></xs:complexType></xs:element>
            """, "(/r/a)[1]");
        var other = Shaped("""
            <xs:element name="r"><xs:complexType><xs:choice><xs:sequence><xs:element name="a" type="xs:int"/><xs:element name="b" type="xs:int"/></xs:sequence><xs:sequence><xs:element name="c" type="xs:int"/><xs:element name="a" type="xs:int"/></xs:sequence></xs:choice></xs:complexType></xs:element>
            """, "(/r/b)[1]");

        Assert.Empty(Field(doc, "v").NullableBecause);
        Assert.Equal([IrNullRule.XmlValue], Rules(other, "v"));
    }

    [Fact]
    public void Namespaces_are_bound_by_the_path_s_prolog()
    {
        var schema = """<xs:element name="r"><xs:complexType><xs:sequence><xs:element name="c" type="xs:int"/></xs:sequence><xs:attribute name="id" type="xs:int" use="required"/></xs:complexType></xs:element>""";
        var tables = $"""
            CREATE XML SCHEMA COLLECTION shop.s AS N'<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="urn:r" elementFormDefault="qualified">{schema}</xs:schema>';
            GO
            CREATE TABLE shop.t (id int NOT NULL PRIMARY KEY, doc xml(DOCUMENT shop.s) NOT NULL);
            GO
            """;
        var doc = Procedure("""
            SELECT t.doc.value('declare default element namespace "urn:r"; (/r/c)[1]', 'int') AS a,
                   t.doc.value('declare namespace n="urn:r"; (/n:r/@id)[1]', 'int') AS b,
                   t.doc.value('(/r/@id)[1]', 'int') AS c
            FROM shop.t AS t;
            """, tables);

        Assert.Empty(Field(doc, "a").NullableBecause);
        Assert.Empty(Field(doc, "b").NullableBecause);
        Assert.Equal([IrNullRule.XmlValue], Rules(doc, "c"));
    }

    // ---- nodes() ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_node_s_required_attribute_is_there_even_in_a_content_instance()
    {
        // The node is a row, so it is there: only the steps below it count.
        var doc = Procedure("""
            SELECT l.n.value('@sku', 'nvarchar(10)') AS sku, l.n.value('(part)[1]', 'nvarchar(10)') AS part,
                   l.n.value('@gift', 'bit') AS gift
            FROM shop.orders AS o CROSS APPLY o.loose.nodes('/order/line') AS l (n);
            """);

        Assert.Empty(Field(doc, "sku").NullableBecause);
        Assert.Empty(Field(doc, "part").NullableBecause);
        Assert.Equal([IrNullRule.XmlValue], Rules(doc, "gift"));
    }

    [Fact]
    public void A_node_of_a_node_is_followed_too()
    {
        var doc = Procedure("""
            SELECT p.n.value('.', 'nvarchar(10)') AS part
            FROM shop.orders AS o CROSS APPLY o.loose.nodes('/order/line') AS l (n) CROSS APPLY l.n.nodes('part') AS p (n);
            """);

        Assert.Empty(Field(doc, "part").NullableBecause);
    }

    [Fact]
    public void Outer_apply_of_nodes_a_document_requires_keeps_no_row_unmatched()
    {
        // Nodes_gives_as_many_rows_as_the_schema_allows.
        var doc = Procedure("""
            SELECT l.n.value('@sku', 'nvarchar(10)') AS sku, b.n.value('@size', 'int') AS size
            FROM shop.orders AS o
                OUTER APPLY o.doc.nodes('/order/line') AS l (n)
                OUTER APPLY o.doc.nodes('/order/box') AS b (n);
            """);

        Assert.Empty(Field(doc, "sku").NullableBecause);
        Assert.Equal([IrNullRule.UnmatchedLateralRow], Rules(doc, "size"));
    }

    [Theory]
    [InlineData("SELECT o.n.value('@id', 'int') AS v FROM @x.nodes('/order') AS o (n);", IrCardinality.One)]
    [InlineData("SELECT b.n.value('@size', 'int') AS v FROM @x.nodes('/order/box') AS b (n);", IrCardinality.AtMostOne)]
    [InlineData("SELECT l.n.value('@sku', 'nvarchar(9)') AS v FROM @x.nodes('/order/line') AS l (n);", IrCardinality.Many)]
    [InlineData("SELECT l.n.value('@sku', 'nvarchar(9)') AS v FROM @x.nodes('/order/line[1]') AS l (n);", IrCardinality.One)]
    [InlineData("SELECT l.n.value('@sku', 'nvarchar(9)') AS v FROM @x.nodes('(/order/line)[1]') AS l (n);", IrCardinality.One)]
    public void Nodes_gives_as_many_rows_as_the_schema_allows(string query, IrCardinality rows)
    {
        var doc = Procedure($"DECLARE @x xml(DOCUMENT shop.order_schema) = N'<order id=\"1\"><customer/><qty>1</qty><line sku=\"a\"><part/></line></order>';\n{query}");

        Assert.Equal(rows, Results(doc)[0].Cardinality);
    }

    [Fact]
    public void Nodes_of_a_nullable_instance_may_give_no_row()
    {
        var doc = Procedure("SELECT o.n.value('@id', 'int') AS v FROM shop.orders AS m OUTER APPLY m.maybe.nodes('/order') AS o (n);");

        Assert.Equal([IrNullRule.UnmatchedLateralRow], Rules(doc, "v"));
    }
}
