using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Model.XmlSchemas;
using Haruspex.SqlServer.Sources;
using Haruspex.SqlServer.Sources.Dacpacs;

namespace Haruspex.Tests.Sources;

/// <summary>
/// <see cref="XmlSchemaCollection"/> as the readers make it: from <c>CREATE XML SCHEMA COLLECTION</c> and
/// <c>ALTER XML SCHEMA COLLECTION … ADD</c> in scripts, and from a package's <c>SqlXmlSchemaCollection</c> elements, and the
/// collection a typed <c>xml</c> names (<see cref="SqlType.Collection"/>) in each. Why:
/// docs/decisions/0133-an-xml-value-is-a-kind-of-its-own-a-branded-string.md.
/// </summary>
public class XmlSchemaCollectionTests
{
    private const string Draft = """<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"><xs:element name="draft"/></xs:schema>""";

    private const string Notes = """<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="urn:notes"><xs:element name="notes"/></xs:schema>""";

    private static SchemaReadResult Read(params (string File, string Sql)[] files)
    {
        using var dir = new TempDirectory();
        foreach (var (file, sql) in files)
            dir.File(file, sql);
        return SqlScriptFolder.Read(dir.Path);
    }

    [Fact]
    public void A_script_s_collection_is_read_with_the_schemas_added_to_it_in_order()
    {
        var read = Read(
            ("a.sql", $"CREATE XML SCHEMA COLLECTION shop.draft_schema AS N'{Draft}';"),
            ("b.sql", $"ALTER XML SCHEMA COLLECTION shop.draft_schema ADD N'{Notes}';"));

        var collection = Assert.Single(read.Model.XmlSchemaCollections);
        Assert.Equal(new ObjectName("shop", "draft_schema"), collection.Name);
        Assert.Equal([Draft, Notes], collection.Schemas);
        Assert.Same(collection, read.Model.FindXmlSchemaCollection(new ObjectName("SHOP", "Draft_Schema")));
    }

    [Fact]
    public void A_schema_that_is_no_literal_leaves_the_collection_s_schemas_unknown()
    {
        var read = Read(
            ("a.sql", $"CREATE XML SCHEMA COLLECTION shop.draft_schema AS N'{Draft}';"),
            ("b.sql", "DECLARE @s nvarchar(max) = N'';\nALTER XML SCHEMA COLLECTION shop.draft_schema ADD @s;"));

        Assert.Empty(Assert.Single(read.Model.XmlSchemaCollections).Schemas);
    }

    [Fact]
    public void An_addition_to_a_collection_the_source_lacks_adds_none()
    {
        var read = Read(("b.sql", $"ALTER XML SCHEMA COLLECTION shop.draft_schema ADD N'{Notes}';"));

        Assert.Empty(read.Model.XmlSchemaCollections);
        Assert.Empty(read.Diagnostics);
    }

    [Fact]
    public void A_collection_created_twice_is_reported_as_defined_twice()
    {
        var read = Read(
            ("a.sql", $"CREATE XML SCHEMA COLLECTION shop.draft_schema AS N'{Draft}';"),
            ("b.sql", $"CREATE XML SCHEMA COLLECTION shop.draft_schema AS N'{Notes}';"));

        Assert.Equal(SourceCodes.DefinedTwice, Assert.Single(read.Diagnostics).Code);
    }

    [Fact]
    public void A_typed_xml_column_names_its_collection_as_written()
    {
        var read = Read(("t.sql", "CREATE TABLE shop.pattern (a xml NULL, b xml(shop.draft_schema) NULL, c xml(DOCUMENT draft_schema) NULL, d XML(CONTENT shop.draft_schema) NULL);"));
        var columns = read.Model.FindTable(new ObjectName("shop", "pattern"))!.Columns;

        Assert.Equal(["xml", "xml(shop.draft_schema)", "xml(DOCUMENT draft_schema)", "xml(shop.draft_schema)"], columns.Select(c => c.Type!.ToString()));
        Assert.Null(columns[0].Type!.Collection);
        Assert.True(columns[2].Type!.Collection!.Document);
    }

    [Fact]
    public void A_package_s_collections_and_typed_xml_are_read_as_its_folder_s()
    {
        var package = DacpacReader.Read(Path.Combine(RepositoryPaths.Root, "tests", "dacpacs", "fixtures", "loom.dacpac")).Model;
        var folder = SqlScriptFolder.Read(Path.Combine(RepositoryPaths.Fixtures, "loom", "database")).Model;

        Assert.Equal(folder.XmlSchemaCollections.Select(c => (c.Name, c.Schemas)), package.XmlSchemaCollections.Select(c => (c.Name, c.Schemas)));
        Assert.Equal(folder.XmlSchemaCollections.Select(c => c.Declarations), package.XmlSchemaCollections.Select(c => c.Declarations));
        Assert.All(package.XmlSchemaCollections, c => Assert.NotNull(c.Declarations));
        var pattern = package.FindTable(new ObjectName("loom", "pattern"))!;
        Assert.Equal(
            ["xml", "xml(loom.draft_schema)", "xml(DOCUMENT dbo.card_schema)"],
            pattern.Columns.Where(c => c.Type is { Kind: SqlTypeKind.Xml }).Skip(1).Select(c => c.Type!.ToString()));
    }

    // ---- declarations (decision 0139) ------------------------------------------------------------------------------------

    private static XmlSchemaDeclarations? Declarations(string sql) =>
        Assert.Single(Read(("a.sql", sql)).Model.XmlSchemaCollections).Declarations;

    [Fact]
    public void A_collection_s_schemas_are_compiled_together_with_those_added_to_it()
    {
        var read = Read(
            ("a.sql", $"CREATE XML SCHEMA COLLECTION shop.draft_schema AS N'{Draft}';"),
            ("b.sql", $"ALTER XML SCHEMA COLLECTION shop.draft_schema ADD N'{Notes}';"));

        var declarations = Assert.Single(read.Model.XmlSchemaCollections).Declarations!;
        Assert.Equal([new XmlName("", "draft"), new XmlName("urn:notes", "notes")], declarations.Globals.Select(e => e.Name));
    }

    [Fact]
    public void One_text_may_hold_several_schemas_after_an_xml_declaration()
    {
        // As a package's one literal does, and as CREATE XML SCHEMA COLLECTION takes.
        var declarations = Declarations($"CREATE XML SCHEMA COLLECTION shop.s AS N'<?xml version=\"1.0\" encoding=\"UTF-8\"?>{Draft}{Notes}';");

        Assert.Equal(2, declarations!.Globals.Count);
    }

    [Theory]
    [InlineData("<xs:schema xmlns:xs=\"http://www.w3.org/2001/XMLSchema\"><xs:element name=\"a\" type=\"missing\"/></xs:schema>")]
    [InlineData("<xs:schema xmlns:xs=\"http://www.w3.org/2001/XMLSchema\"><xs:element name=\"a\"></xs:schema>")]
    [InlineData("<other/>")]
    public void A_collection_that_does_not_compile_has_no_declarations(string schema) =>
        Assert.Null(Declarations($"CREATE XML SCHEMA COLLECTION shop.s AS N'{schema}';"));

    [Fact]
    public void A_type_s_facets_are_its_own_and_its_bases_with_the_built_in_range()
    {
        var declarations = Declarations("""
            CREATE XML SCHEMA COLLECTION shop.s AS N'<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
             <xs:simpleType name="code"><xs:restriction base="xs:token"><xs:maxLength value="4"/><xs:enumeration value=" a  b "/><xs:enumeration value="c"/></xs:restriction></xs:simpleType>
             <xs:simpleType name="small"><xs:restriction base="xs:unsignedByte"><xs:maxExclusive value="100"/></xs:restriction></xs:simpleType>
             <xs:simpleType name="smaller"><xs:restriction base="small"><xs:minInclusive value="3"/></xs:restriction></xs:simpleType>
             <xs:element name="r"><xs:complexType><xs:attribute name="c" type="code" use="required"/><xs:attribute name="n" type="smaller" default="5"/></xs:complexType></xs:element>
            </xs:schema>';
            """)!;

        var root = Assert.IsType<XmlComplexTypeDeclaration>(declarations.Types[Assert.Single(declarations.Globals).Type]);
        Assert.Equal([XmlAttributeUse.Required, XmlAttributeUse.Defaulted], root.Attributes.Select(a => a.Use));
        var code = Assert.IsType<XmlSimpleTypeDeclaration>(declarations.Types[root.Attributes[0].Type]);
        Assert.Equal(XmlValueKind.NormalisedText, code.Kind);
        Assert.Equal(new XmlFacets(Enumeration: ["a b", "c"], MaxLength: 4), code.Facets);
        var smaller = Assert.IsType<XmlSimpleTypeDeclaration>(declarations.Types[root.Attributes[1].Type]);
        Assert.Equal(XmlValueKind.WholeNumber, smaller.Kind);
        Assert.Equal(new XmlFacets(MinInclusive: "3", MaxInclusive: "255", MaxExclusive: "100"), smaller.Facets);
    }

    [Fact]
    public void A_type_another_derives_from_and_a_substitution_group_s_head_are_marked()
    {
        var declarations = Declarations("""
            CREATE XML SCHEMA COLLECTION shop.s AS N'<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
             <xs:complexType name="base"><xs:sequence><xs:element name="p" type="xs:int"/></xs:sequence></xs:complexType>
             <xs:complexType name="more"><xs:complexContent><xs:extension base="base"><xs:sequence><xs:element name="q" type="xs:int"/></xs:sequence></xs:extension></xs:complexContent></xs:complexType>
             <xs:element name="head" type="base"/>
             <xs:element name="member" type="more" substitutionGroup="head"/>
            </xs:schema>';
            """)!;

        var head = Assert.Single(declarations.Globals, e => e.Name.Local == "head");
        Assert.True(head.HeadsGroup);
        Assert.True(Assert.IsType<XmlComplexTypeDeclaration>(declarations.Types[head.Type]).Replaceable);
        var member = Assert.Single(declarations.Globals, e => e.Name.Local == "member");
        Assert.False(member.HeadsGroup);
        Assert.False(Assert.IsType<XmlComplexTypeDeclaration>(declarations.Types[member.Type]).Replaceable);
    }
}
