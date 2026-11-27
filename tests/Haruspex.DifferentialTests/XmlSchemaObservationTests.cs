using Haruspex.DifferentialTests.Server;

namespace Haruspex.DifferentialTests;

/// <summary>
/// What SQL Server's validation of a typed <c>xml</c> instance against its schema collection proves (decision 0139): which nodes
/// are always there, how many, and where the schema's word doesn't hold (<c>CONTENT</c>, a second global element, <c>xsi:nil</c>,
/// <c>xsi:type</c>, substitution groups, wildcards). The rules rest on these (Haruspex.SqlServer.Inference's README.md, "XML values").
/// Observed on SQL Server 2025 RTM-CU10. Every fixture is invented.
/// </summary>
public class XmlSchemaObservationTests(SqlServerFixture server)
{
    private Task<CallOutcome> Run(string call, params string[] setup) => ObservedCall.Run(server, setup, call);

    /// <summary>An order requires its customer, a line or more, each with a sku, and an id; its note is optional, its qty nillable.</summary>
    private static readonly string[] Orders =
    [
        """
        CREATE XML SCHEMA COLLECTION dbo.obs_order_schema AS N'<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
         <xs:element name="order"><xs:complexType>
          <xs:sequence>
           <xs:element name="customer" type="xs:string"/>
           <xs:element name="note" type="xs:string" minOccurs="0"/>
           <xs:element name="qty" nillable="true" type="xs:int"/>
           <xs:element name="line" maxOccurs="3"><xs:complexType><xs:attribute name="sku" type="xs:string" use="required"/></xs:complexType></xs:element>
           <xs:element name="size" minOccurs="0" default="M" type="xs:string"/>
          </xs:sequence>
          <xs:attribute name="id" type="xs:int" use="required"/>
          <xs:attribute name="channel" type="xs:string" default="web"/>
          <xs:attribute name="kind" type="xs:string" fixed="retail"/>
         </xs:complexType></xs:element>
        </xs:schema>';
        """,
        "CREATE TABLE dbo.obs_order (n int NOT NULL PRIMARY KEY, doc xml(DOCUMENT dbo.obs_order_schema) NOT NULL, loose xml(dbo.obs_order_schema) NULL);",
        """
        INSERT dbo.obs_order VALUES
            (1, N'<order id="7"><customer></customer><qty xsi:nil="true" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"/><line sku="a"/></order>', N''),
            (2, N'<order id="8"><customer>Ann</customer><qty>3</qty><line sku="b"/><line sku="c"/><size/></order>',
                N'<order id="1"><customer/><qty>1</qty><line sku="x"/></order><order id="2"><customer/><qty>1</qty><line sku="y"/></order>');
        """,
    ];

    [Fact]
    public async Task A_required_node_s_value_is_never_null_in_a_typed_instance()
    {
        // A required attribute and element are there in every DOCUMENT instance; an empty string element is '', not NULL; a path
        // without [1] is taken where the schema proves one node. An optional element is NULL where it is missing.
        Assert.Equal(
            "n,id,customer,bare,note: 1,7,,7,null;2,8,Ann,8,null",
            (await Run("SELECT n, doc.value('(/order/@id)[1]', 'int') AS id, doc.value('(/order/customer)[1]', 'nvarchar(10)') AS customer, doc.value('/order/@id', 'int') AS bare, doc.value('(/order/note)[1]', 'nvarchar(10)') AS note FROM dbo.obs_order ORDER BY n;", Orders)).ToString());
    }

    [Fact]
    public async Task A_nil_element_s_value_is_null()
    {
        Assert.Equal(
            "n,qty,here: 1,null,True;2,3,True",
            (await Run("SELECT n, doc.value('(/order/qty)[1]', 'int') AS qty, doc.exist('/order/qty') AS here FROM dbo.obs_order ORDER BY n;", Orders)).ToString());
    }

    [Fact]
    public async Task A_nil_element_has_no_children_but_keeps_its_attributes()
    {
        // batch requires k, but a nil batch has none: value() is NULL, nodes() gives no row and exist() is 0. Its own required
        // attribute is there.
        Assert.Equal(
            "k,id,ks,here: null,4,0,False",
            (await Run(
                "DECLARE @x xml(DOCUMENT dbo.obs_nil) = N'<r><batch id=\"4\" xsi:nil=\"true\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"/></r>'; SELECT @x.value('(/r/batch/k)[1]', 'int') AS k, @x.value('(/r/batch/@id)[1]', 'int') AS id, (SELECT COUNT(*) FROM @x.nodes('/r/batch/k') AS t(c)) AS ks, @x.exist('/r/batch/k') AS here;",
                """CREATE XML SCHEMA COLLECTION dbo.obs_nil AS N'<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"><xs:element name="r"><xs:complexType><xs:sequence><xs:element name="batch" nillable="true"><xs:complexType><xs:sequence><xs:element name="k" type="xs:int"/></xs:sequence><xs:attribute name="id" type="xs:int" use="required"/></xs:complexType></xs:element></xs:sequence></xs:complexType></xs:element></xs:schema>';""")).ToString());
    }

    [Fact]
    public async Task A_default_or_fixed_attribute_is_written_into_the_instance()
    {
        // An attribute's default or fixed value is written where it is missing; an element's default only fills an empty element.
        Assert.Equal(
            "n,channel,kind,size: 1,web,retail,null;2,web,retail,M",
            (await Run("SELECT n, doc.value('(/order/@channel)[1]', 'nvarchar(10)') AS channel, doc.value('(/order/@kind)[1]', 'nvarchar(10)') AS kind, doc.value('(/order/size)[1]', 'nvarchar(10)') AS size FROM dbo.obs_order ORDER BY n;", Orders)).ToString());
    }

    [Fact]
    public async Task A_content_instance_may_be_empty_or_have_several_top_level_elements()
    {
        Assert.Equal(
            "n,id,orders: 1,null,0;2,1,2",
            (await Run("SELECT n, loose.value('(/order/@id)[1]', 'int') AS id, (SELECT COUNT(*) FROM o.loose.nodes('/order') AS t(c)) AS orders FROM dbo.obs_order AS o ORDER BY n;", Orders)).ToString());
        Assert.Equal(6901, (await Run("DECLARE @d xml(DOCUMENT dbo.obs_order_schema) = N'';", Orders)).Error);
    }

    [Fact]
    public async Task Nodes_gives_as_many_rows_as_the_schema_allows()
    {
        // A line or more in each order, each with its sku; an attribute is a node too.
        Assert.Equal(
            "n,lines,ids: 1,1,1;2,2,1",
            (await Run("SELECT n, (SELECT COUNT(*) FROM o.doc.nodes('/order/line') AS t(c)) AS lines, (SELECT COUNT(*) FROM o.doc.nodes('/order/@id') AS t(c)) AS ids FROM dbo.obs_order AS o ORDER BY n;", Orders)).ToString());
        Assert.Equal(
            "sku: x;y",
            (await Run("SELECT l.c.value('@sku', 'nvarchar(5)') AS sku FROM dbo.obs_order AS o CROSS APPLY o.loose.nodes('/order/line') AS l(c) ORDER BY sku;", Orders)).ToString());
    }

    [Fact]
    public async Task Value_of_an_element_of_complex_content_is_refused()
    {
        // Mixed content too: value() reads only an attribute, or an element of a simple type or simple content.
        Assert.Equal(9314, (await Run(
            "DECLARE @x xml(DOCUMENT dbo.obs_mixed) = N'<m/>'; SELECT @x.value('(/m)[1]', 'nvarchar(10)') AS v;",
            """CREATE XML SCHEMA COLLECTION dbo.obs_mixed AS N'<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"><xs:element name="m"><xs:complexType mixed="true"><xs:sequence><xs:element name="i" type="xs:string" minOccurs="0"/></xs:sequence></xs:complexType></xs:element></xs:schema>';""")).Error);
    }

    [Fact]
    public async Task A_document_s_root_is_any_global_element_of_any_schema_in_the_collection()
    {
        // Two global elements in one schema, or a second added by ALTER … ADD in another namespace: either may be the root.
        Assert.Equal(
            "v,e: null,False",
            (await Run(
                "DECLARE @x xml(DOCUMENT dbo.obs_two) = N'<b k=\"1\"/>'; SELECT @x.value('(/a/@id)[1]', 'int') AS v, @x.exist('/a') AS e;",
                """CREATE XML SCHEMA COLLECTION dbo.obs_two AS N'<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"><xs:element name="a"><xs:complexType><xs:attribute name="id" type="xs:int" use="required"/></xs:complexType></xs:element><xs:element name="b"><xs:complexType><xs:attribute name="k" type="xs:int" use="required"/></xs:complexType></xs:element></xs:schema>';""")).ToString());
        Assert.Equal(
            "v: null",
            (await Run(
                "DECLARE @x xml(DOCUMENT dbo.obs_one) = N'<o:a xmlns:o=\"urn:other\"/>'; SELECT @x.value('(/a/@id)[1]', 'int') AS v;",
                """CREATE XML SCHEMA COLLECTION dbo.obs_one AS N'<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"><xs:element name="a"><xs:complexType><xs:attribute name="id" type="xs:int" use="required"/></xs:complexType></xs:element></xs:schema>';""",
                """ALTER XML SCHEMA COLLECTION dbo.obs_one ADD N'<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="urn:other"><xs:element name="a"/></xs:schema>';""")).ToString());
    }

    [Fact]
    public async Task An_instance_may_name_a_derived_type_in_xsi_type()
    {
        // The element c is declared of type base; an instance gives it the extension more, whose content is longer.
        Assert.Equal(
            "p,q: 1,2",
            (await Run(
                "DECLARE @x xml(DOCUMENT dbo.obs_derive) = N'<r><c xsi:type=\"more\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"><p>1</p><q>2</q></c></r>'; SELECT @x.value('(/r/c/p)[1]', 'int') AS p, @x.value('(/r/c/q)[1]', 'int') AS q;",
                """
                CREATE XML SCHEMA COLLECTION dbo.obs_derive AS N'<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
                 <xs:complexType name="base"><xs:sequence><xs:element name="p" type="xs:int"/></xs:sequence></xs:complexType>
                 <xs:complexType name="more"><xs:complexContent><xs:extension base="base"><xs:sequence><xs:element name="q" type="xs:int"/></xs:sequence></xs:extension></xs:complexContent></xs:complexType>
                 <xs:element name="r"><xs:complexType><xs:sequence><xs:element name="c" type="base"/></xs:sequence></xs:complexType></xs:element>
                </xs:schema>';
                """)).ToString());
    }

    [Fact]
    public async Task A_substitution_group_s_member_stands_where_its_head_is_declared()
    {
        Assert.Equal(
            "head,member: null,3",
            (await Run(
                "DECLARE @x xml(DOCUMENT dbo.obs_subst) = N'<r><member>3</member></r>'; SELECT @x.value('(/r/head)[1]', 'int') AS head, @x.value('(/r/member)[1]', 'int') AS member;",
                """
                CREATE XML SCHEMA COLLECTION dbo.obs_subst AS N'<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
                 <xs:element name="head" type="xs:int"/>
                 <xs:element name="member" type="xs:int" substitutionGroup="head"/>
                 <xs:element name="r"><xs:complexType><xs:sequence><xs:element ref="head"/></xs:sequence></xs:complexType></xs:element>
                </xs:schema>';
                """)).ToString());
    }

    private const string Wildcards = """
        CREATE XML SCHEMA COLLECTION dbo.obs_any AS N'<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
         <xs:element name="r"><xs:complexType>
          <xs:sequence><xs:any processContents="skip" namespace="##any"/><xs:element name="b" type="xs:int"/></xs:sequence>
          <xs:attribute name="id" type="xs:int" use="required"/><xs:anyAttribute processContents="skip"/>
         </xs:complexType></xs:element>
        </xs:schema>';
        """;

    [Fact]
    public async Task A_wildcard_may_take_an_element_of_a_declared_name_before_it()
    {
        // The first b is the wildcard's, not validated as xs:int.
        Assert.Equal(
            "b: text",
            (await Run("DECLARE @x xml(DOCUMENT dbo.obs_any) = N'<r id=\"1\"><b>text</b><b>5</b></r>'; SELECT @x.value('(/r/b)[1]', 'nvarchar(10)') AS b;", Wildcards)).ToString());
    }

    [Fact]
    public async Task A_declared_attribute_is_validated_beside_an_attribute_wildcard() => Assert.Equal(6926, (await Run("DECLARE @x xml(DOCUMENT dbo.obs_any) = N'<r id=\"x\"><c/><b>5</b></r>';", Wildcards)).Error);

    // ---- the value's facets ----------------------------------------------------------------------------------------------

    /// <summary>Numbers and strings of facets: a decimal, a big integer, a bounded decimal, a collapsed token, a bounded string and a boolean.</summary>
    private static readonly string[] Values =
    [
        """
        CREATE XML SCHEMA COLLECTION dbo.obs_values AS N'<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
         <xs:element name="n"><xs:complexType>
          <xs:attribute name="dec" type="xs:decimal" use="required"/>
          <xs:attribute name="big" type="xs:integer" use="required"/>
          <xs:attribute name="pos" use="required"><xs:simpleType><xs:restriction base="xs:decimal"><xs:minExclusive value="0"/><xs:maxInclusive value="9.99"/></xs:restriction></xs:simpleType></xs:attribute>
          <xs:attribute name="tok" use="required"><xs:simpleType><xs:restriction base="xs:token"><xs:maxLength value="3"/></xs:restriction></xs:simpleType></xs:attribute>
          <xs:attribute name="str" use="required"><xs:simpleType><xs:restriction base="xs:string"><xs:minLength value="2"/><xs:maxLength value="4"/></xs:restriction></xs:simpleType></xs:attribute>
          <xs:attribute name="b" type="xs:boolean" use="required"/>
         </xs:complexType></xs:element>
        </xs:schema>';
        """,
        "CREATE TABLE dbo.obs_values (n int NOT NULL PRIMARY KEY, x xml(DOCUMENT dbo.obs_values) NOT NULL);",
        """
        INSERT dbo.obs_values VALUES
            (1, N'<n dec="2.7" big="99999999999" pos="0.004" tok="  a   b " str="&#xE9;&#xE9;" b="1"/>'),
            (2, N'<n dec="-3" big="5" pos="9.99" tok="x" str="&#x1F600;&#x1F600;" b="false"/>');
        """,
    ];

    [Fact]
    public async Task Value_converts_the_canonical_text_as_cast_converts_nvarchar()
    {
        // The text is the value's canonical form: a token collapsed, a boolean true or false. Converted, a decimal is rounded to
        // the target's places (0.004 as decimal(5,2) is 0.00, past the schema's exclusive 0), a string is cut in UTF-16 code units, two emoji to three (one and half another).
        Assert.Equal(
            "n,tok,b,bit,pos1,pos2,units3: 1,a b,true,True,0.0,0.00,2;2,x,false,False,10.0,9.99,3",
            (await Run("SELECT n, x.value('(/n/@tok)[1]', 'nvarchar(10)') AS tok, x.value('(/n/@b)[1]', 'nvarchar(10)') AS b, x.value('(/n/@b)[1]', 'bit') AS bit, x.value('(/n/@pos)[1]', 'decimal(5,1)') AS pos1, x.value('(/n/@pos)[1]', 'decimal(5,2)') AS pos2, DATALENGTH(x.value('(/n/@str)[1]', 'nvarchar(3)')) / 2 AS units3 FROM dbo.obs_values ORDER BY n;", Values)).ToString());

        // Text that isn't a whole number fails as int (245), and one too big for it fails (248): an error, never NULL.
        Assert.Equal(245, (await Run("SELECT x.value('(/n/@dec)[1]', 'int') AS v FROM dbo.obs_values WHERE n = 1;", Values)).Error);
        Assert.Equal(245, (await Run("SELECT x.value('(/n/@pos)[1]', 'int') AS v FROM dbo.obs_values WHERE n = 2;", Values)).Error);
        Assert.Equal(248, (await Run("SELECT x.value('(/n/@big)[1]', 'int') AS v FROM dbo.obs_values WHERE n = 1;", Values)).Error);
        Assert.Equal("v: -3", (await Run("SELECT x.value('(/n/@dec)[1]', 'int') AS v FROM dbo.obs_values WHERE n = 2;", Values)).ToString());
    }

    [Fact]
    public async Task Xml_s_methods_require_ansi_warnings() =>
        // So no overflow is ever NULL: under ANSI_WARNINGS off the query is refused (1934).
        Assert.Equal(1934, (await Run("SET ANSI_WARNINGS OFF; SET ARITHABORT OFF; SELECT x.value('(/n/@big)[1]', 'int') AS v FROM dbo.obs_values;", Values)).Error);

    [Fact]
    public async Task A_length_facet_counts_utf_16_code_units()
    {
        // minLength 2 and maxLength 4: one emoji is two code units and passes; two emoji and a letter are five and fail.
        Assert.Equal(
            "ok: 1",
            (await Run("DECLARE @x xml(DOCUMENT dbo.obs_values) = N'<n dec=\"1\" big=\"1\" pos=\"1\" tok=\"a\" str=\"&#x1F600;\" b=\"1\"/>'; SELECT 1 AS ok;", Values)).ToString());
        Assert.Equal(6926, (await Run("DECLARE @x xml(DOCUMENT dbo.obs_values) = N'<n dec=\"1\" big=\"1\" pos=\"1\" tok=\"a\" str=\"&#x1F600;&#x1F600;a\" b=\"1\"/>';", Values)).Error);
        Assert.Equal(6926, (await Run("DECLARE @x xml(DOCUMENT dbo.obs_values) = N'<n dec=\"1\" big=\"1\" pos=\"1\" tok=\"a\" str=\"&#xE9;&#xE9;&#xE9;&#xE9;&#xE9;\" b=\"1\"/>';", Values)).Error);
    }

    [Fact]
    public async Task A_group_s_own_occurrences_scale_what_it_holds()
    {
        // A required a in an optional sequence may be missing; a sequence that may repeat holds its d twice.
        Assert.Equal(
            "a,ds: null,2",
            (await Run(
                "DECLARE @x xml(DOCUMENT dbo.obs_groups) = N'<r><d>1</d><d>2</d></r>'; SELECT @x.value('(/r/a)[1]', 'int') AS a, (SELECT COUNT(*) FROM @x.nodes('/r/d') AS t(c)) AS ds;",
                """CREATE XML SCHEMA COLLECTION dbo.obs_groups AS N'<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"><xs:element name="r"><xs:complexType><xs:sequence><xs:sequence minOccurs="0"><xs:element name="a" type="xs:int"/></xs:sequence><xs:sequence maxOccurs="2"><xs:element name="d" type="xs:int"/></xs:sequence></xs:sequence></xs:complexType></xs:element></xs:schema>';""")).ToString());
    }

    [Fact]
    public async Task A_decimal_is_rounded_half_away_from_zero()
    {
        // So an inclusive bound with more places than the target is crossed: 99.995 is 100.00, and -0.005 is -0.01.
        Assert.Equal(
            "hi,lo: 100.00,-0.01",
            (await Run(
                "DECLARE @x xml(DOCUMENT dbo.obs_round) = N'<r hi=\"99.995\" lo=\"-0.005\"/>'; SELECT @x.value('(/r/@hi)[1]', 'decimal(5,2)') AS hi, @x.value('(/r/@lo)[1]', 'decimal(4,2)') AS lo;",
                """CREATE XML SCHEMA COLLECTION dbo.obs_round AS N'<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"><xs:element name="r"><xs:complexType><xs:attribute name="hi" use="required"><xs:simpleType><xs:restriction base="xs:decimal"><xs:maxInclusive value="99.995"/></xs:restriction></xs:simpleType></xs:attribute><xs:attribute name="lo" use="required"><xs:simpleType><xs:restriction base="xs:decimal"><xs:minInclusive value="-0.005"/></xs:restriction></xs:simpleType></xs:attribute></xs:complexType></xs:element></xs:schema>';""")).ToString());
    }
}
