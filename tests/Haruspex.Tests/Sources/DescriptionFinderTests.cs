using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Sources;
using Haruspex.SqlServer.Sources.Dacpacs;

namespace Haruspex.Tests.Sources;

/// <summary>
/// Which extended properties are descriptions, of what, and what is passed over with a note rather than guessed
/// (docs/decisions/0128-a-column-s-description-reaches-the-fields-that-are-that-column.md).
/// </summary>
public class DescriptionFinderTests
{
    private const string Kennel = """
        CREATE TABLE kennel.dog (dog_id int NOT NULL, name nvarchar(20) NULL);
        GO
        CREATE VIEW kennel.dogs AS SELECT d.dog_id, d.name AS called FROM kennel.dog AS d;
        GO
        CREATE PROCEDURE kennel.get_dog @dog_id int AS SELECT d.dog_id, d.name FROM kennel.dog AS d WHERE d.dog_id = @dog_id;
        GO

        """;

    private static readonly ObjectName Dog = new("kennel", "dog");
    private static readonly ObjectName Dogs = new("kennel", "dogs");
    private static readonly ObjectName GetDog = new("kennel", "get_dog");

    private static (Descriptions Descriptions, IReadOnlyList<Diagnostic> Notes) Find(string sql, params string[] names)
    {
        using var dir = new TempDirectory();
        dir.File("kennel.sql", Kennel + sql);
        var read = SqlScriptFolder.Read(dir.Path);
        Assert.DoesNotContain(read.Diagnostics, d => d.IsError);
        var (model, notes) = DescriptionFinder.Find(read.Model, names.Length == 0 ? DescriptionFinder.DefaultNames : names);
        return (model.Descriptions, notes);
    }

    [Fact]
    public void A_table_view_procedure_column_and_parameter_each_take_their_own()
    {
        var (descriptions, notes) = Find("""
            EXEC sp_addextendedproperty 'MS_Description', 'A dog.', 'SCHEMA', 'kennel', 'TABLE', 'dog';
            EXEC sp_addextendedproperty 'MS_Description', 'What the dog answers to.', 'SCHEMA', 'kennel', 'TABLE', 'dog', 'COLUMN', 'name';
            EXEC sp_addextendedproperty 'MS_Description', 'Every dog.', 'SCHEMA', 'kennel', 'VIEW', 'dogs';
            EXEC sp_addextendedproperty 'MS_Description', 'The name, as the view calls it.', 'SCHEMA', 'kennel', 'VIEW', 'dogs', 'COLUMN', 'called';
            EXEC sp_addextendedproperty 'MS_Description', 'Finds one dog.', 'SCHEMA', 'kennel', 'PROCEDURE', 'get_dog';
            EXEC sp_addextendedproperty 'MS_Description', 'The dog to find.', 'SCHEMA', 'kennel', 'PROCEDURE', 'get_dog', 'PARAMETER', '@dog_id';
            """);

        Assert.Empty(notes);
        Assert.Equal("A dog.", descriptions.Of(Dog));
        Assert.Equal("What the dog answers to.", descriptions.Of(Dog, "name"));
        Assert.Equal("Every dog.", descriptions.Of(Dogs));
        Assert.Equal("The name, as the view calls it.", descriptions.Of(Dogs, "called"));
        Assert.Equal("Finds one dog.", descriptions.Of(GetDog));
        Assert.Equal("The dog to find.", descriptions.Of(GetDog, "@dog_id"));
        Assert.Null(descriptions.Of(Dog, "dog_id"));
    }

    [Fact]
    public void Level_types_and_names_are_matched_ignoring_case_as_the_server_matches_them()
    {
        var (descriptions, notes) = Find("EXEC sp_addextendedproperty 'ms_description', 'The name.', 'schema', 'KENNEL', 'table', 'DOG', 'column', 'NAME';");

        Assert.Empty(notes);
        Assert.Equal("The name.", descriptions.Of(Dog, "name"));
    }

    [Fact]
    public void Only_the_configured_names_are_descriptions()
    {
        const string Sql = """
            EXEC sp_addextendedproperty @name = N'Description', @value = N'A dog.', @level0type = N'SCHEMA', @level0name = N'kennel', @level1type = N'TABLE', @level1name = N'dog';
            EXEC sp_addextendedproperty 'Caption', 'Dog', 'SCHEMA', 'kennel', 'TABLE', 'dog', 'COLUMN', 'name';
            """;

        Assert.Equal(0, Find(Sql).Descriptions.Count);
        var (configured, notes) = Find(Sql, "MS_Description", "Description");
        Assert.Empty(notes);
        Assert.Equal("A dog.", configured.Of(Dog));
        Assert.Null(configured.Of(Dog, "name"));
    }

    [Fact]
    public void A_property_of_another_name_is_passed_over_without_a_word_whatever_its_arguments()
    {
        var (descriptions, notes) = Find("EXEC sp_addextendedproperty 'Caption', @caption, 'SCHEMA', 'kennel', 'TABLE', 'ghost';");

        Assert.Equal(0, descriptions.Count);
        Assert.Empty(notes);
    }

    [Fact]
    public void A_description_on_what_no_output_has_is_passed_over_without_a_word()
    {
        var (descriptions, notes) = Find("""
            EXEC sp_addextendedproperty 'MS_Description', 'Dogs.', 'SCHEMA', 'kennel';
            EXEC sp_addextendedproperty 'MS_Description', 'The database.';
            EXEC sp_addextendedproperty 'MS_Description', 'An index.', 'SCHEMA', 'kennel', 'TABLE', 'dog', 'INDEX', 'ix_name';
            """);

        Assert.Equal(0, descriptions.Count);
        Assert.Empty(notes);
    }

    [Theory]
    [InlineData(
        "EXEC sp_addextendedproperty 'MS_Description', @text, 'SCHEMA', 'kennel', 'TABLE', 'dog';",
        "The description of kennel.dog is passed over: its argument @value isn't a string literal, an identifier or NULL.")]
    [InlineData(
        "EXEC sp_addextendedproperty @which, 'A dog.', 'SCHEMA', 'kennel', 'TABLE', 'dog';",
        "The description of kennel.dog is passed over: its argument @name isn't a string literal, an identifier or NULL.")]
    [InlineData(
        "EXEC sp_addextendedproperty 'MS_Description', 42, 'SCHEMA', 'kennel', 'TABLE', 'dog';",
        "The description of kennel.dog is passed over: its value isn't text.")]
    [InlineData(
        "EXEC sp_addextendedproperty 'MS_Description', 'A cat.', 'SCHEMA', 'kennel', 'TABLE', 'cat';",
        "The description of kennel.cat is passed over: the source doesn't define kennel.cat.")]
    [InlineData(
        "EXEC sp_addextendedproperty 'MS_Description', 'Its breed.', 'SCHEMA', 'kennel', 'TABLE', 'dog', 'COLUMN', 'breed';",
        "The description of kennel.dog.breed is passed over: the source doesn't define kennel.dog.breed.")]
    [InlineData(
        "EXEC sp_addextendedproperty 'MS_Description', 'Which dog.', 'SCHEMA', 'kennel', 'PROCEDURE', 'get_dog', 'PARAMETER', 'dog_id';",
        "The description of kennel.get_dog.dog_id is passed over: the source doesn't define kennel.get_dog.dog_id.")]
    [InlineData(
        "EXEC sp_updateextendedproperty 'MS_Description', 'A good dog.', 'SCHEMA', 'kennel', 'TABLE', 'dog';",
        "The description of kennel.dog is updated here, and what stands depends on the order the scripts run, so it has none.")]
    public void A_description_that_can_t_be_read_for_certain_is_passed_over_saying_why(string sql, string why)
    {
        var (descriptions, notes) = Find(sql);

        Assert.Equal(0, descriptions.Count);
        var note = Assert.Single(notes);
        Assert.Equal((SourceCodes.DescriptionNotRead, why), (note.Code, note.Message));
    }

    [Fact]
    public void A_description_set_twice_has_none_and_the_second_is_named()
    {
        // The server refuses the second add (15233: observed); a folder doesn't say which script runs first.
        var (descriptions, notes) = Find("""
            EXEC sp_addextendedproperty 'MS_Description', 'A dog.', 'SCHEMA', 'kennel', 'TABLE', 'dog';
            EXEC sp_addextendedproperty 'ms_description', 'A hound.', 'SCHEMA', 'kennel', 'TABLE', 'dog';
            """);

        Assert.Null(descriptions.Of(Dog));
        var note = Assert.Single(notes);
        Assert.Equal("The description of kennel.dog is set more than once, so which stands isn't known, and it has none.", note.Message);
        Assert.Equal(Kennel.Split('\n').Length + 1, Assert.IsType<SourceLocation>(note.At).Line);
    }

    [Fact]
    public void A_dropped_description_has_none()
    {
        var (descriptions, notes) = Find("""
            EXEC sp_addextendedproperty 'MS_Description', 'A dog.', 'SCHEMA', 'kennel', 'TABLE', 'dog';
            EXEC sp_dropextendedproperty 'MS_Description', 'SCHEMA', 'kennel', 'TABLE', 'dog';
            """);

        Assert.Null(descriptions.Of(Dog));
        Assert.Equal("The description of kennel.dog is dropped here, and what stands depends on the order the scripts run, so it has none.", Assert.Single(notes).Message);
    }

    [Fact]
    public void A_package_gives_the_descriptions_its_project_s_scripts_give()
    {
        var dacpacs = Path.Combine(RepositoryPaths.Root, "tests", "dacpacs");
        using var dir = new TempDirectory();
        File.Copy(Path.Combine(dacpacs, "projects", "lectern", "lectern.sql"), Path.Combine(dir.Path, "lectern.sql"));
        var folder = DescriptionFinder.Find(SqlScriptFolder.Read(dir.Path).Model, DescriptionFinder.DefaultNames);
        var package = DescriptionFinder.Find(DacpacReader.Read(Path.Combine(dacpacs, "lectern", "lectern.dacpac")).Model, DescriptionFinder.DefaultNames);

        var book = new ObjectName("library", "book");
        var shelf = new ObjectName("library", "shelf");
        var find = new ObjectName("library", "find_book");
        foreach (var (model, notes) in new[] { folder, package })
        {
            Assert.Empty(notes);
            Assert.Equal(7, model.Descriptions.Count);
            Assert.Equal("A book the library holds.", model.Descriptions.Of(book));
            Assert.Equal("The book's title, as printed. It may hold */ and \"quotes\".", model.Descriptions.Of(book, "title"));
            Assert.Equal("Where the book is shelved.\nTwo lines.", model.Descriptions.Of(book, "shelf_mark")?.ReplaceLineEndings("\n"));
            Assert.Equal("The books on the shelves.", model.Descriptions.Of(shelf));
            Assert.Equal("The shelf mark, as the view names it.", model.Descriptions.Of(shelf, "mark"));
            Assert.Equal("Finds a book by its title.", model.Descriptions.Of(find));
            Assert.Equal("The title to find.", model.Descriptions.Of(find, "@title"));
        }
    }
}
