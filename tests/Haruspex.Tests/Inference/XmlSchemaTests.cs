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

    // ---- the value's facets ----------------------------------------------------------------------------------------------

    private const string Facets = """
        <xs:simpleType name="size"><xs:restriction base="xs:string"><xs:enumeration value="S"/><xs:enumeration value="M"/><xs:enumeration value="XL"/></xs:restriction></xs:simpleType>
        <xs:simpleType name="code"><xs:restriction base="xs:token"><xs:minLength value="2"/><xs:maxLength value="4"/></xs:restriction></xs:simpleType>
        <xs:simpleType name="positive"><xs:restriction base="xs:int"><xs:minInclusive value="1"/></xs:restriction></xs:simpleType>
        <xs:simpleType name="share"><xs:restriction base="xs:decimal"><xs:minExclusive value="0"/><xs:maxExclusive value="1"/></xs:restriction></xs:simpleType>
        <xs:simpleType name="tenth"><xs:restriction base="share"><xs:fractionDigits value="1"/></xs:restriction></xs:simpleType>
        <xs:simpleType name="step"><xs:restriction base="xs:decimal"><xs:enumeration value="1"/><xs:enumeration value="2.5"/><xs:enumeration value="4"/></xs:restriction></xs:simpleType>
        <xs:element name="r"><xs:complexType>
         <xs:attribute name="size" type="size" use="required"/>
         <xs:attribute name="code" type="code" use="required"/>
         <xs:attribute name="n" type="positive" use="required"/>
         <xs:attribute name="byte" type="xs:unsignedByte"/>
         <xs:attribute name="share" type="share" use="required"/>
         <xs:attribute name="tenth" type="tenth" use="required"/>
         <xs:attribute name="step" type="step" use="required"/>
         <xs:attribute name="when" type="xs:date" use="required"/>
        </xs:complexType></xs:element>
        """;

    private static IrDocument Faceted(string path, string type)
    {
        var tables = $"""
            CREATE XML SCHEMA COLLECTION shop.s AS N'<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">{Facets}</xs:schema>';
            GO
            CREATE TABLE shop.t (id int NOT NULL PRIMARY KEY, doc xml(DOCUMENT shop.s) NOT NULL);
            GO
            """;
        return Procedure($"SELECT t.doc.value('{path}', '{type}') AS v FROM shop.t AS t;", tables);
    }

    private static IrFacets? FacetsOf(IrDocument doc) => Assert.IsType<IrScalar>(Field(doc, "v").Type).Facets;

    private static readonly IrFactSource Size = new(IrFactSourceKind.XmlSchema, "shop.s r/@size");

    [Fact]
    public void A_string_enumeration_is_the_value_s_set_where_the_type_holds_each_item()
    {
        var doc = Faceted("(/r/@size)[1]", "nvarchar(5)");

        Assert.Equal(new IrValueSet([new IrValue("S", true), new IrValue("M", true), new IrValue("XL", true)], IrValueMatch.Exact, [Size]), FacetsOf(doc)!.Values);
        Assert.Equal(IrDocument.CurrentVersion, doc.Version);
    }

    [Fact]
    public void A_string_enumeration_the_type_cuts_is_no_set() =>
        // XL as nvarchar(1) is X: the set would be wrong, so none is claimed.
        Assert.Null(FacetsOf(Faceted("(/r/@size)[1]", "nvarchar(1)"))!.Values);

    [Fact]
    public void String_lengths_hold_in_utf_16_code_units_cut_to_the_type()
    {
        // A_length_facet_counts_utf_16_code_units: as nvarchar counts, so the length is the same kind of fact as the type's.
        var wide = FacetsOf(Faceted("(/r/@code)[1]", "nvarchar(10)"))!;
        Assert.Equal(new IrLength(4, IsFixed: false, IrLengthUnit.Utf16, IrByteEncoding.Unknown, new IrFactSource(IrFactSourceKind.XmlSchema, "shop.s r/@code")), wide.Length);
        Assert.Equal(new IrMinLength(2, new IrFactSource(IrFactSourceKind.XmlSchema, "shop.s r/@code")), wide.MinLength);

        var narrow = FacetsOf(Faceted("(/r/@code)[1]", "nvarchar(1)"))!;
        Assert.Equal(new IrFactSource(IrFactSourceKind.Type, "nvarchar(1)"), narrow.Length!.From);
        Assert.Equal(1, narrow.MinLength!.Value);
    }

    [Theory]
    [InlineData("varchar(10)")]
    [InlineData("nchar(4)")]
    public void Strings_of_other_types_carry_no_facet_of_the_schema(string type)
    {
        // A varchar may change a character to its code page's best fit, and an nchar pads.
        var facets = FacetsOf(Faceted("(/r/@code)[1]", type));

        Assert.True(facets is null || (facets.MinLength?.From.Kind != IrFactSourceKind.XmlSchema && facets.Length?.From.Kind != IrFactSourceKind.XmlSchema));
    }

    [Fact]
    public void A_whole_number_s_bounds_hold_as_an_integer()
    {
        var n = FacetsOf(Faceted("(/r/@n)[1]", "int"))!;
        Assert.Equal(new IrBound("1", true, new IrFactSource(IrFactSourceKind.XmlSchema, "shop.s r/@n")), n.Min);
        Assert.Equal(IrFactSourceKind.Type, n.Max!.From.Kind);

        // The built-in type's range is the schema's too, and holds where the node may be missing.
        var b = Faceted("(/r/@byte)[1]", "int");
        Assert.Equal(["0", "255"], [FacetsOf(b)!.Min!.Value, FacetsOf(b)!.Max!.Value]);
        Assert.Equal([IrNullRule.XmlValue], Rules(b, "v"));
    }

    [Theory]
    [InlineData("(/r/@share)[1]", "decimal(5,1)", "0", true, "1", true)]
    [InlineData("(/r/@share)[1]", "decimal(3,0)", "0", true, "1", true)]
    [InlineData("(/r/@tenth)[1]", "decimal(5,1)", "0", false, "1", false)]
    public void A_decimal_s_bounds_are_widened_where_the_conversion_rounds(string path, string type, string min, bool minInclusive, string max, bool maxInclusive)
    {
        // Value_converts_the_canonical_text_as_cast_converts_nvarchar: 0.004 as decimal(5,2) is 0.00, past an exclusive 0. With
        // fractionDigits no more than the type's places, nothing is rounded.
        var facets = FacetsOf(Faceted(path, type))!;

        Assert.Equal((min, minInclusive, max, maxInclusive), (facets.Min!.Value, facets.Min.Inclusive, facets.Max!.Value, facets.Max.Inclusive));
        Assert.Equal(IrFactSourceKind.XmlSchema, facets.Min.From.Kind);
    }

    [Theory]
    [InlineData("int", new[] { "1", "4" })]
    [InlineData("decimal(5,1)", new[] { "1", "2.5", "4" })]
    public void A_number_s_enumeration_keeps_what_the_type_takes_exactly(string type, string[] items) =>
        // As int, text that isn't a whole number fails (245), so 2.5 is never a value.
        Assert.Equal(items, FacetsOf(Faceted("(/r/@step)[1]", type))!.Values!.Items.Select(i => i.Text));

    [Fact]
    public void A_number_s_enumeration_the_type_rounds_is_no_set()
    {
        var facets = FacetsOf(Faceted("(/r/@step)[1]", "decimal(5,0)"));

        Assert.True(facets?.Values is null || facets.Values.From.All(f => f.Kind != IrFactSourceKind.XmlSchema));
    }

    [Theory]
    [InlineData("(/r/@n)[1]", "float")]
    [InlineData("(/r/@n)[1]", "nvarchar(10)")]
    [InlineData("(/r/@when)[1]", "date")]
    public void Other_conversions_carry_no_facet_of_the_schema(string path, string type)
    {
        var doc = Faceted(path, type);

        Assert.NotEqual(IrDocument.CurrentVersion, doc.Version);
    }

    [Fact]
    public void A_document_with_no_fact_of_a_schema_is_labelled_0_34()
    {
        // The label holds: a value() read from XML, with no facet of the schema, adds nothing 0.35 says.
        var doc = Value("(/order/note)[1]", "nvarchar(max)");

        Assert.Equal([IrNullRule.XmlValue], Rules(doc, "v"));
        Assert.Equal("0.34", doc.Version);
    }

    // ---- occurrences, rounding, other numbers and namespaces -------------------------------------------------------------

    private static IrDocument ShapedAs(string schema, string path, string type, string targetNamespace = "", string? nodes = null)
    {
        var target = targetNamespace.Length == 0 ? "" : $" targetNamespace=\"{targetNamespace}\" elementFormDefault=\"qualified\"";
        var tables = $"""
            CREATE XML SCHEMA COLLECTION shop.s AS N'<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"{target}>{schema}</xs:schema>';
            GO
            CREATE TABLE shop.t (id int NOT NULL PRIMARY KEY, doc xml(DOCUMENT shop.s) NOT NULL);
            GO
            """;
        var read = nodes is null ? "" : $"\nDECLARE @x xml(DOCUMENT shop.s) = (SELECT TOP (1) t.doc FROM shop.t AS t);\nSELECT n.c.value('.', 'int') AS v FROM @x.nodes('{nodes}') AS n (c);";
        return Procedure($"SELECT t.doc.value('{path}', '{type}') AS v FROM shop.t AS t;{read}", tables);
    }

    [Fact]
    public void A_group_s_own_occurrences_scale_what_it_holds()
    {
        // A_group_s_own_occurrences_scale_what_it_holds: a required a inside an optional sequence may be missing, and a d inside a
        // sequence that may repeat may be there twice.
        var optional = ShapedAs("""<xs:element name="r"><xs:complexType><xs:sequence minOccurs="0"><xs:element name="a" type="xs:int"/></xs:sequence></xs:complexType></xs:element>""", "(/r/a)[1]", "int");
        var repeated = ShapedAs("""<xs:element name="r"><xs:complexType><xs:sequence maxOccurs="2"><xs:element name="d" type="xs:int"/></xs:sequence></xs:complexType></xs:element>""", "(/r/d)[1]", "int", nodes: "/r/d");

        Assert.Equal([IrNullRule.XmlValue], Rules(optional, "v"));
        Assert.Empty(Field(repeated, "v").NullableBecause);
        Assert.Equal(IrCardinality.Many, repeated.Objects[0].Results![1].Cardinality);
    }

    [Theory]
    [InlineData("<xs:maxInclusive value=\"99.995\"/>", "max", "100", true)]
    [InlineData("<xs:minInclusive value=\"-0.005\"/>", "min", "-0.01", true)]
    public void An_inclusive_bound_with_more_places_than_the_decimal_is_rounded_outward(string facet, string side, string value, bool inclusive)
    {
        // A_decimal_is_rounded_half_away_from_zero: 99.995 as decimal(5,2) is 100.00, and -0.005 as decimal(4,2) is -0.01.
        var doc = ShapedAs($"""<xs:element name="r"><xs:complexType><xs:attribute name="p" use="required"><xs:simpleType><xs:restriction base="xs:decimal">{facet}</xs:restriction></xs:simpleType></xs:attribute></xs:complexType></xs:element>""", "(/r/@p)[1]", side == "max" ? "decimal(5,2)" : "decimal(4,2)");
        var facets = Assert.IsType<IrScalar>(Field(doc, "v").Type).Facets!;
        var bound = side == "max" ? facets.Max! : facets.Min!;

        Assert.Equal((value, inclusive, IrFactSourceKind.XmlSchema), (bound.Value, bound.Inclusive, bound.From.Kind));
    }

    [Theory]
    [InlineData("bit")]
    [InlineData("money")]
    [InlineData("float")]
    [InlineData("real")]
    public void A_number_read_as_another_kind_of_number_carries_no_facet_of_the_schema(string type)
    {
        // xs:int from 1 up, read as bit, is 1 whatever the number; money and floats aren't read.
        var facets = FacetsOf(Faceted("(/r/@n)[1]", type));

        Assert.True(facets is null || new[] { facets.Min?.From, facets.Max?.From }.All(f => f?.Kind != IrFactSourceKind.XmlSchema));
    }

    [Fact]
    public void A_child_is_matched_by_its_namespace_and_its_local_name_together()
    {
        // Two children named c, the unqualified one required and the qualified one optional.
        const string Schema = """<xs:element name="r"><xs:complexType><xs:sequence><xs:element name="c" form="unqualified" type="xs:int"/><xs:element name="c" type="xs:int" minOccurs="0"/></xs:sequence></xs:complexType></xs:element>""";
        var qualified = ShapedAs(Schema, "declare default element namespace \"urn:r\"; (/r/c)[1]", "int", "urn:r");
        var unqualified = ShapedAs(Schema, "declare namespace n=\"urn:r\"; (/n:r/c)[1]", "int", "urn:r");

        Assert.Equal([IrNullRule.XmlValue], Rules(qualified, "v"));
        Assert.Empty(Field(unqualified, "v").NullableBecause);
    }
}
