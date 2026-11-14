using Haruspex.SqlServer.Inference;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;
using Haruspex.SqlServer.Sources;

namespace Haruspex.Tests.Semantics;

/// <summary>
/// <see cref="SchemaGaps"/>: a name that binds to nothing, in a schema read with parts left unread, says where it may be
/// defined, so that one missing package isn't chased name by name.
/// </summary>
public class SchemaGapsTests
{
    private static DatabaseModel Model(params UnreadPart[] unread) =>
        new([], [], [], [], [], [], [], [], []) { Unread = [.. unread] };

    [Fact]
    public void A_schema_read_whole_adds_nothing_to_a_message() => Assert.Equal("", SchemaGaps.MayBeIn(Model()));

    [Fact]
    public void Each_part_left_unread_is_named_as_where_a_missing_name_may_be() =>
        Assert.Equal(
            " It may be defined in crane.dacpac, which harbour.dacpac refers to and which was not found; or in quay.dacpac, which crane.dacpac refers to and which was not found.",
            SchemaGaps.MayBeIn(Model(new UnreadPart("crane.dacpac", "harbour.dacpac"), new UnreadPart("quay.dacpac", "crane.dacpac"))));

    [Fact]
    public void A_table_that_binds_to_nothing_names_the_part_it_may_be_in()
    {
        using var dir = new TempDirectory();
        dir.File("p.sql", "CREATE PROCEDURE dbo.p AS SELECT b.id FROM dock.berth AS b;");
        var read = SqlScriptFolder.Read(dir.Path).Model;
        var model = new DatabaseModel(read.Tables, read.UserTypes, read.Procedures, read.Functions, read.Views, read.Synonyms, read.Triggers, read.CascadingKeys, read.TableRedefinitions)
        {
            Unread = [new UnreadPart("quay.dacpac", "crane.dacpac")],
        };

        var doc = IrInference.Infer(model, [new ObjectName("dbo", "p")]).Value;

        Assert.Contains(doc.Unknowns, u => u.Detail.EndsWith("It may be defined in quay.dacpac, which crane.dacpac refers to and which was not found.", StringComparison.Ordinal));
    }
}
