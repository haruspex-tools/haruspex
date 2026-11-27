using Haruspex.Ir;

namespace Haruspex.Tests.Inference;

/// <summary>
/// Which fields take a column's description: only one that is exactly that column, unchanged, never one derived from several
/// columns or an expression (docs/decisions/0128-a-column-s-description-reaches-the-fields-that-are-that-column.md).
/// </summary>
public class DescriptionTests
{
    private const string Kennel = """
        CREATE TABLE kennel.dog (dog_id int NOT NULL PRIMARY KEY, name nvarchar(20) NOT NULL, owner_id int NULL);
        GO
        CREATE TABLE kennel.owner (owner_id int NOT NULL PRIMARY KEY, name nvarchar(40) NOT NULL);
        GO
        CREATE VIEW kennel.named AS SELECT d.dog_id, d.name FROM kennel.dog AS d;
        GO
        CREATE VIEW kennel.called AS SELECT d.dog_id, d.name AS called FROM kennel.dog AS d;
        GO
        EXEC sp_addextendedproperty 'MS_Description', 'What the dog answers to.', 'SCHEMA', 'kennel', 'TABLE', 'dog', 'COLUMN', 'name';
        GO
        EXEC sp_addextendedproperty 'MS_Description', 'The dog''s own number.', 'SCHEMA', 'kennel', 'TABLE', 'dog', 'COLUMN', 'dog_id';
        GO
        EXEC sp_addextendedproperty 'MS_Description', 'The owner''s full name.', 'SCHEMA', 'kennel', 'TABLE', 'owner', 'COLUMN', 'name';
        GO
        EXEC sp_addextendedproperty 'MS_Description', 'The name as the second view gives it.', 'SCHEMA', 'kennel', 'VIEW', 'called', 'COLUMN', 'called';
        GO

        """;

    private static IrDocument Infer(string body) => InferProcedure.Infer("kennel.p", ("kennel.sql", Kennel + "CREATE PROCEDURE kennel.p @dog_id int AS\n" + body));

    /// <summary>Each field of the first result's row, by name, with its description.</summary>
    private static Dictionary<string, string?> Described(IrDocument document) =>
        document.Types.Values.Single(t => t.Identity?.Path == "results[0][]").Fields.ToDictionary(f => f.Name, f => f.Description);

    [Fact]
    public void A_field_that_is_a_column_takes_its_description_under_any_alias()
    {
        var fields = Described(Infer("SELECT d.dog_id, d.name AS called, o.name FROM kennel.dog AS d LEFT JOIN kennel.owner AS o ON o.owner_id = d.owner_id;"));

        Assert.Equal("The dog's own number.", fields["dog_id"]);
        Assert.Equal("What the dog answers to.", fields["called"]);

        // From the outer join's nullable side, still the owner's column, and not the dog's of the same name.
        Assert.Equal("The owner's full name.", fields["name"]);
    }

    [Fact]
    public void A_column_through_a_star_takes_its_description()
    {
        var fields = Described(Infer("SELECT * FROM kennel.dog AS d;"));

        Assert.Equal(("The dog's own number.", "What the dog answers to.", null), (fields["dog_id"], fields["name"], fields["owner_id"]));
    }

    [Theory]
    [InlineData("SELECT UPPER(d.name) AS name FROM kennel.dog AS d;")]
    [InlineData("SELECT d.name COLLATE Latin1_General_100_BIN2 AS name FROM kennel.dog AS d;")]
    [InlineData("SELECT (d.name) + N'' AS name FROM kennel.dog AS d;")]
    [InlineData("SELECT COALESCE(o.name, d.name) AS name FROM kennel.dog AS d LEFT JOIN kennel.owner AS o ON o.owner_id = d.owner_id;")]
    [InlineData("SELECT CASE WHEN d.owner_id IS NULL THEN d.name ELSE o.name END AS name FROM kennel.dog AS d LEFT JOIN kennel.owner AS o ON o.owner_id = d.owner_id;")]
    [InlineData("SELECT d.name FROM kennel.dog AS d UNION ALL SELECT o.name FROM kennel.owner AS o;")]
    [InlineData("WITH r AS (SELECT d.dog_id, d.name FROM kennel.dog AS d UNION ALL SELECT r.dog_id + 1, o.name FROM r JOIN kennel.owner AS o ON o.owner_id = r.dog_id) SELECT r.name FROM r;")]
    public void A_field_derived_from_an_expression_or_several_columns_takes_none(string body) =>
        Assert.Null(Described(Infer(body))["name"]);

    [Theory]
    [InlineData("SELECT d.name FROM kennel.dog AS d UNION SELECT d.name FROM kennel.dog AS d;")]
    [InlineData("WITH r AS (SELECT d.dog_id, d.name FROM kennel.dog AS d UNION ALL SELECT r.dog_id + 1, d.name FROM r JOIN kennel.dog AS d ON d.dog_id = r.dog_id + 1) SELECT r.name FROM r;")]
    public void A_set_whose_every_query_reads_the_same_column_keeps_its_description(string body) =>
        Assert.Equal("What the dog answers to.", Described(Infer(body))["name"]);

    [Fact]
    public void Results_of_two_paths_that_read_different_columns_take_none()
    {
        // The rows differ in length (40 and 20), so the result is a union of the two; an owner's short name matches the dog's
        // row too, so neither variant may say which column it holds.
        var document = Infer("""
            IF @dog_id IS NULL
                SELECT o.name FROM kennel.owner AS o;
            ELSE
                SELECT d.name FROM kennel.dog AS d WHERE d.dog_id = @dog_id;
            """);

        Assert.Equal(2, document.Types.Count);
        Assert.All(document.Types.Values, t => Assert.Null(t.Fields.Single().Description));
    }

    [Fact]
    public void Results_of_two_paths_that_read_the_same_column_keep_it()
    {
        var fields = Described(Infer("""
            IF @dog_id IS NULL
                SELECT d.name FROM kennel.dog AS d;
            ELSE
                SELECT d.name FROM kennel.dog AS d WHERE d.dog_id = @dog_id;
            """));

        Assert.Equal("What the dog answers to.", fields["name"]);
    }

    [Fact]
    public void A_column_unchanged_through_a_derived_table_or_cte_keeps_its_description()
    {
        var fields = Described(Infer("""
            WITH n AS (SELECT d.dog_id, d.name FROM kennel.dog AS d)
            SELECT n.name, x.loud FROM n CROSS APPLY (SELECT UPPER(n.name) AS loud) AS x;
            """));

        Assert.Equal("What the dog answers to.", fields["name"]);
        Assert.Null(fields["loud"]);
    }

    [Fact]
    public void A_view_s_own_description_of_a_column_replaces_the_table_s()
    {
        var plain = Described(Infer("SELECT v.name FROM kennel.named AS v;"));
        var own = Described(Infer("SELECT v.called FROM kennel.called AS v;"));

        Assert.Equal("What the dog answers to.", plain["name"]);
        Assert.Equal("The name as the second view gives it.", own["called"]);
    }

    [Fact]
    public void A_key_of_for_json_that_is_a_column_takes_its_description()
    {
        var document = Infer("SELECT d.dog_id AS id, UPPER(d.name) AS loud FROM kennel.dog AS d FOR JSON PATH;");
        var row = document.Types.Values.Single(t => t.Fields.Any(f => f.Name == "loud"));

        Assert.Equal("The dog's own number.", row.Fields.Single(f => f.Name == "id").Description);
        Assert.Null(row.Fields.Single(f => f.Name == "loud").Description);
    }

    [Fact]
    public void A_procedure_and_its_parameter_take_their_own()
    {
        var document = InferProcedure.Infer("kennel.p", ("kennel.sql", Kennel + """
            CREATE PROCEDURE kennel.p @dog_id int, @owner_id int = NULL AS SELECT d.name FROM kennel.dog AS d WHERE d.dog_id = @dog_id;
            GO
            EXEC sp_addextendedproperty 'MS_Description', 'Finds one dog.', 'SCHEMA', 'kennel', 'PROCEDURE', 'p';
            GO
            EXEC sp_addextendedproperty 'MS_Description', 'The dog to find.', 'SCHEMA', 'kennel', 'PROCEDURE', 'p', 'PARAMETER', '@dog_id';
            """));

        var procedure = Assert.Single(document.Objects);
        Assert.Equal("Finds one dog.", procedure.Description);
        Assert.Equal(["The dog to find.", null], procedure.Parameters.Select(p => p.Description));
        Assert.Equal(IrDocument.DescribedVersion, document.Version);
    }

    [Fact]
    public void A_document_that_records_no_description_keeps_its_older_label()
    {
        var document = Infer("SELECT UPPER(d.name) AS name FROM kennel.dog AS d;");

        Assert.Null(IrDocumentDescribed(document));
        Assert.Equal(IrDocument.FoldersVersion, IrDocument.ReadableVersions[3]);
        Assert.NotEqual(IrDocument.DescribedVersion, document.Version);
    }

    private static string? IrDocumentDescribed(IrDocument document) =>
        document.Objects.Select(o => o.Description).Concat(document.Types.Values.SelectMany(t => t.Fields).Select(f => f.Description)).FirstOrDefault(d => d is not null);
}
