using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Sources;
using Haruspex.SqlServer.Sources.Dacpacs;

namespace Haruspex.Tests.Sources;

// A full-text index, which model.xml keeps in an element of its own that names its table and its key index.
public partial class DacpacReaderTests
{
    private static readonly Lazy<DacpacReadResult> Glossary = new(() => DacpacReader.Read(Path.Combine(Packages, "fixtures", "glossary.dacpac")));

    [Theory]
    [InlineData("entry", "pk_entry")]
    [InlineData("note", "ux_note_code")]
    [InlineData("label", "uq_label_ref")]
    public void A_full_text_index_s_key_is_read_from_a_package_as_from_its_folder(string table, string key)
    {
        // KeyName names a constraint by its schema and name ([lex].[pk_entry]) and an index by its table's too
        // ([lex].[note].[ux_note_code]); either is the index's name.
        var name = new ObjectName("lex", table);
        var folder = SqlScriptFolder.Read(Path.Combine(RepositoryPaths.Fixtures, "glossary", "database")).Model;

        Assert.Empty(Glossary.Value.Diagnostics);
        Assert.Equal([key], Glossary.Value.Model.FindTable(name)!.FullTextKeys.Select(k => k.Index));
        Assert.Equal(folder.FindTable(name)!.FullTextKeys.Select(k => k.Index), Glossary.Value.Model.FindTable(name)!.FullTextKeys.Select(k => k.Index));
    }

    [Fact]
    public void A_full_text_index_without_a_key_name_gives_its_table_no_key()
    {
        using var folder = new TempDirectory();
        var path = Path.Combine(folder.Path, "index.dacpac");
        TestPackage.Write(path, TestPackage.Table("dbo", "page", "id int", "words nvarchar(10)") + """
            <Element Type="SqlFullTextIndex" Name="[dbo].[page]">
              <Relationship Name="IndexedObject"><Entry><References Name="[dbo].[page]" /></Entry></Relationship>
            </Element>
            """);

        Assert.Empty(DacpacReader.Read(path).Model.FindTable(new ObjectName("dbo", "page"))!.FullTextKeys);
    }
}
