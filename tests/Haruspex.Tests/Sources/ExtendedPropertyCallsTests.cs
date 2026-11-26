using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Sources;

namespace Haruspex.Tests.Sources;

/// <summary>
/// How a script's extended property calls are read: by position, by name, or both, and what is left unread rather than guessed
/// (docs/decisions/0128-a-column-s-description-reaches-the-fields-that-are-that-column.md).
/// </summary>
public class ExtendedPropertyCallsTests
{
    private static ExtendedProperty Read(string sql)
    {
        using var dir = new TempDirectory();
        dir.File("p.sql", sql);
        return Assert.Single(SqlScriptFolder.Read(dir.Path).Model.ExtendedProperties);
    }

    [Fact]
    public void Named_arguments_are_read_by_their_names_in_any_order()
    {
        var property = Read("""
            EXECUTE sp_addextendedproperty @level0type = N'SCHEMA', @name = N'Description', @value = 'Formal name of the town',
                @level0name = N'Atlas', @level1type = N'TABLE', @level1name = N'Towns', @level2type = N'COLUMN', @level2name = N'TownName';
            """);

        Assert.Equal(("Description", "Formal name of the town"), (property.Name, property.Value));
        Assert.Equal([new("SCHEMA", "Atlas"), new("TABLE", "Towns"), new("COLUMN", "TownName")], property.Levels);
        Assert.Null(property.Unread);
        Assert.Equal(PropertyAction.Add, property.Action);
    }

    [Fact]
    public void Positional_arguments_are_name_value_then_each_level_s_type_and_name()
    {
        // The server takes an identifier as a string argument, as an install script passes level names (observed).
        var property = Read("EXECUTE [sys].[sp_addextendedproperty] N'MS_Description', N'Second address line.', N'SCHEMA', [Atlas], N'TABLE', [Address], N'COLUMN', [Line2];");

        Assert.Equal(("MS_Description", "Second address line."), (property.Name, property.Value));
        Assert.Equal([new("SCHEMA", "Atlas"), new("TABLE", "Address"), new("COLUMN", "Line2")], property.Levels);
        Assert.Null(property.Unread);
    }

    [Fact]
    public void Positional_arguments_may_be_followed_by_named_ones()
    {
        var property = Read("EXEC sp_addextendedproperty 'MS_Description', @value = 'A dog.', @level0type = 'SCHEMA', @level0name = 'kennel', @level1type = 'TABLE', @level1name = 'dog';");

        Assert.Equal(("MS_Description", "A dog."), (property.Name, property.Value));
        Assert.Equal([new("SCHEMA", "kennel"), new("TABLE", "dog")], property.Levels);
    }

    [Fact]
    public void Levels_end_at_the_first_null_type()
    {
        var property = Read("EXEC sys.sp_addextendedproperty N'MS_Description', N'Dogs and their owners.', N'SCHEMA', [kennel], NULL, NULL, NULL, NULL;");

        Assert.Equal([new PropertyLevel("SCHEMA", "kennel")], property.Levels);
        Assert.Null(property.Unread);
    }

    [Theory]
    [InlineData("EXEC sp_addextendedproperty 'MS_Description', @text, 'SCHEMA', 'kennel', 'TABLE', 'dog';", "Its argument @value isn't a string literal, an identifier or NULL.")]
    [InlineData("EXEC sp_addextendedproperty @name = @which, @value = 'x', @level0type = 'SCHEMA', @level0name = 'kennel';", "Its argument @name isn't a string literal, an identifier or NULL.")]
    [InlineData("EXEC sp_addextendedproperty 'MS_Description', 'x', 'SCHEMA', 'kennel', 'TABLE', @table;", "Its argument @level1name isn't a string literal, an identifier or NULL.")]
    [InlineData("EXEC sp_addextendedproperty 'MS_Description', 42, 'SCHEMA', 'kennel', 'TABLE', 'dog';", "Its value isn't text.")]
    [InlineData("EXEC sp_addextendedproperty 'MS_Description', 'x', 'SCHEMA', 'kennel', 'TABLE', NULL, 'COLUMN', 'name';", "Its argument @level1name is NULL, so what it is set on isn't one object.")]
    [InlineData("EXEC sp_addextendedproperty @title = 'MS_Description', @value = 'x';", "Its argument 1 isn't one the procedure takes.")]
    public void An_argument_that_isn_t_a_literal_leaves_the_property_unread_saying_which(string sql, string why) =>
        Assert.Equal(why, Read(sql).Unread);

    [Fact]
    public void An_update_and_a_drop_are_read_as_what_they_do()
    {
        using var dir = new TempDirectory();
        dir.File("p.sql", """
            EXEC sp_updateextendedproperty 'MS_Description', 'A good dog.', 'SCHEMA', 'kennel', 'TABLE', 'dog';
            EXEC sys.sp_dropextendedproperty 'MS_Description', 'SCHEMA', 'kennel', 'TABLE', 'dog';
            """);

        var properties = SqlScriptFolder.Read(dir.Path).Model.ExtendedProperties;

        Assert.Equal([(PropertyAction.Update, "A good dog."), (PropertyAction.Drop, null)], properties.Select(p => (p.Action, p.Value)));
        Assert.All(properties, p => Assert.Equal([new("SCHEMA", "kennel"), new("TABLE", "dog")], p.Levels));
    }

    [Theory]
    [InlineData("master")]
    [InlineData("Kennel")]
    public void A_call_through_a_database_is_unread_since_it_may_run_in_another(string database) =>
        Assert.Equal(
            $"Its call names the database {database}, which isn't known to be this one: a call through a database runs there.",
            Read($"EXEC {database}.sys.sp_addextendedproperty 'MS_Description', 'x', 'SCHEMA', 'kennel', 'TABLE', 'dog';").Unread);

    [Fact]
    public void A_procedure_of_that_name_in_the_database_s_own_schema_is_not_the_system_s()
    {
        using var dir = new TempDirectory();
        dir.File("p.sql", "EXEC kennel.sp_addextendedproperty 'MS_Description', 'x', 'SCHEMA', 'kennel', 'TABLE', 'dog';");

        Assert.Empty(SqlScriptFolder.Read(dir.Path).Model.ExtendedProperties);
    }
}
