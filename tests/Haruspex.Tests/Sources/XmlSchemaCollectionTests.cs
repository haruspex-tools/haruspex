using Haruspex.SqlServer.Model;
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
        var pattern = package.FindTable(new ObjectName("loom", "pattern"))!;
        Assert.Equal(
            ["xml", "xml(loom.draft_schema)", "xml(DOCUMENT dbo.card_schema)"],
            pattern.Columns.Where(c => c.Type is { Kind: SqlTypeKind.Xml }).Skip(1).Select(c => c.Type!.ToString()));
    }
}
