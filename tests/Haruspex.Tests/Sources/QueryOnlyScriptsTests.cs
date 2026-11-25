using Haruspex.SqlServer.Sources;

namespace Haruspex.Tests.Sources;

/// <summary>
/// Which scripts add nothing to a database read from a folder (<see cref="QueryOnlyScripts"/>): what the folder reader passes over
/// does not count, and what it records does, because leaving it out could claim more than the database holds.
/// </summary>
public class QueryOnlyScriptsTests
{
    private static bool QueryOnly(string sql)
    {
        using var folder = new TempDirectory();
        folder.File("s.sql", sql);
        return QueryOnlyScripts.Is(Path.Combine(folder.Path, "s.sql"));
    }

    /// <summary>What an application's query file holds, which the folder reader passes over.</summary>
    [Theory]
    [InlineData("SELECT p.id FROM garden.plant AS p;")]
    [InlineData("WITH p AS (SELECT 1 AS id) SELECT p.id FROM p;")]
    [InlineData("INSERT INTO garden.water_log (plant_id, watered_at) VALUES (@plant_id, SYSUTCDATETIME());")]
    [InlineData("UPDATE garden.plant SET height = @height WHERE id = @id;")]
    [InlineData("DELETE FROM garden.plant WHERE id = @id;")]
    [InlineData("MERGE garden.plant AS t USING (SELECT @id AS id) AS s ON t.id = s.id WHEN NOT MATCHED THEN INSERT (id) VALUES (s.id);")]
    [InlineData("SET NOCOUNT ON; SELECT 1 AS x;")]
    [InlineData("SELECT p.id INTO garden.plant_copy FROM garden.plant AS p;")]
    [InlineData("DECLARE @n int = 1; EXEC garden.list_plants;")]
    [InlineData("-- nothing yet")]
    [InlineData("")]
    public void A_script_the_folder_reader_passes_over_adds_nothing(string sql) => Assert.True(QueryOnly(sql));

    /// <summary>What the folder reader records: a definition, a change to one, anything that may disable a constraint, an error.</summary>
    [Theory]
    [InlineData("CREATE TABLE garden.plant (id int NOT NULL);")]
    [InlineData("CREATE PROCEDURE garden.p AS SELECT 1 AS one;")]
    [InlineData("ALTER TABLE garden.plant NOCHECK CONSTRAINT ck_height;")]
    [InlineData("ALTER TABLE garden.plant ADD note nvarchar(20) NULL;")]
    [InlineData("DROP TABLE garden.plant;")]
    [InlineData("ALTER PROCEDURE garden.p AS SELECT 2 AS two;")]
    [InlineData("BULK INSERT garden.plant FROM 'plants.csv';")]
    [InlineData("INSERT INTO garden.plant WITH (IGNORE_CONSTRAINTS) (id) SELECT id FROM OPENROWSET(BULK 'plants.csv', SINGLE_CLOB) AS b;")]
    [InlineData("SELECT p.id FROM garden.plant AS p;\nGO\nDROP TABLE garden.plant;")]
    [InlineData("SELECT p.id FROM garden.plant AS p WHERE")]
    public void A_script_the_folder_reader_records_is_the_database_s(string sql) => Assert.False(QueryOnly(sql));

    /// <summary>
    /// Every record of <c>SchemaDeclarations</c> is one <see cref="SchemaDeclarations.IsEmpty"/> counts. A record added there
    /// changes this list, which says to count it too: a script that only makes that record would otherwise be left out of init's
    /// source.
    /// </summary>
    [Fact]
    public void Every_record_of_the_declarations_is_counted_as_declaring_something()
    {
        var records = typeof(SchemaDeclarations).GetProperties().Where(p => p.Name != nameof(SchemaDeclarations.IsEmpty)).Select(p => p.Name).Order(StringComparer.Ordinal);

        Assert.Equal(
            [
                "AddedKeys", "CascadingKeys", "ConditionalSynonyms", "Deployed", "Diagnostics", "Enforcement", "FunctionChanges", "Functions",
                "KeyDisables", "MaskChanges", "PeriodDrops", "ProcedureChanges", "Procedures", "Synonyms", "TableChanges", "Tables", "Triggers",
                "UnreadDeployments", "UserTypes", "Views",
            ],
            records);
    }
}
