using Haruspex.Diagnostics;
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
    /// One script for each record a folder's script can make, which makes that record alone: so each record is counted by
    /// <see cref="SchemaDeclarations.IsEmpty"/>, since leaving one out there lets its script through as adding nothing.
    /// </summary>
    public static TheoryData<string, string> OneRecordEach => new()
    {
        { "Diagnostics", "SELECT p.id FROM garden.plant AS p WHERE" },
        { "Tables", "CREATE TABLE garden.plant (id int NOT NULL);" },
        { "TableChanges", "ALTER TABLE garden.plant ADD note nvarchar(20) NULL;" },
        { "PeriodDrops", "ALTER TABLE garden.plant DROP PERIOD FOR SYSTEM_TIME;" },
        { "AddedKeys", "ALTER TABLE garden.plant ADD CONSTRAINT pk_plant PRIMARY KEY (id);" },
        { "FullTextKeys", "CREATE FULLTEXT INDEX ON garden.plant (name) KEY INDEX pk_plant;" },
        { "UserTypes", "CREATE TYPE garden.height FROM decimal(5, 2) NOT NULL;" },
        { "Procedures", "CREATE PROCEDURE garden.p AS SELECT 1 AS one;" },
        { "ProcedureChanges", "ALTER PROCEDURE garden.p AS SELECT 2 AS two;" },
        { "Functions", "CREATE FUNCTION garden.f() RETURNS int AS BEGIN RETURN 1; END;" },
        { "FunctionChanges", "ALTER FUNCTION garden.f() RETURNS int AS BEGIN RETURN 2; END;" },
        { "Views", "CREATE VIEW garden.v AS SELECT 1 AS one;" },
        { "Synonyms", "CREATE SYNONYM garden.s FOR garden.plant;" },
        { "ConditionalSynonyms", "IF 1 = 1 CREATE SYNONYM garden.s FOR garden.plant;" },
        { "Triggers", "CREATE TRIGGER garden.t ON garden.plant AFTER INSERT AS SELECT 1 AS one;" },
        { "CascadingKeys", "ALTER TABLE garden.water_log ADD CONSTRAINT fk_plant FOREIGN KEY (plant_id) REFERENCES garden.plant (id) ON DELETE CASCADE;" },
        { "Enforcement", "ALTER TABLE garden.plant NOCHECK CONSTRAINT ck_height;" },
        { "MaskChanges", "ALTER TABLE garden.plant ALTER COLUMN name ADD MASKED WITH (FUNCTION = 'default()');" },
        { "KeyDisables", "ALTER INDEX ix_plant ON garden.plant DISABLE;" },
        { "ExtendedProperties", "EXEC sp_addextendedproperty N'MS_Description', N'A plant.', N'SCHEMA', N'garden', N'TABLE', N'plant';" },
    };

    /// <summary>The records only a project's pre- or post-deployment script makes, which a folder's script never is.</summary>
    private static readonly string[] DeploymentOnly = ["Deployed", "UnreadDeployments"];

    [Theory]
    [MemberData(nameof(OneRecordEach))]
    public void A_script_that_makes_one_record_alone_is_the_database_s(string record, string sql)
    {
        using var folder = new TempDirectory();
        folder.File("s.sql", sql);
        var declarations = new SchemaDeclarations();
        SqlScriptFolder.ReadInto(declarations, ScriptFile.InOrder(folder.Path, ["s.sql"], listed: true));

        Assert.Equal([record], Records(declarations));
        Assert.False(QueryOnlyScripts.Is(Path.Combine(folder.Path, "s.sql")));
    }

    /// <summary>
    /// The scripts above cover every record of <c>SchemaDeclarations</c> but the deployment scripts' own, so a record added there
    /// needs a script here.
    /// </summary>
    [Fact]
    public void Every_record_a_folder_s_script_can_make_has_a_script_that_makes_it()
    {
        var covered = OneRecordEach.Select(row => row.Data.Item1).Concat(DeploymentOnly).Order(StringComparer.Ordinal);

        Assert.Equal(RecordNames().Order(StringComparer.Ordinal), covered);
    }

    /// <summary>The records the declarations hold something in; for <c>Diagnostics</c>, an error.</summary>
    private static List<string> Records(SchemaDeclarations declarations) =>
    [
        .. RecordNames().Where(name => typeof(SchemaDeclarations).GetProperty(name)!.GetValue(declarations) switch
        {
            List<Diagnostic> diagnostics => diagnostics.Any(d => d.IsError),
            System.Collections.ICollection collection => collection.Count > 0,
            _ => throw new InvalidOperationException($"{name} is not a collection."),
        }),
    ];

    private static IEnumerable<string> RecordNames() =>
        typeof(SchemaDeclarations).GetProperties().Where(p => p.Name != nameof(SchemaDeclarations.IsEmpty)).Select(p => p.Name);

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
                "AddedKeys", "CascadingKeys", "ConditionalSynonyms", "Deployed", "Diagnostics", "Enforcement", "ExtendedProperties", "FullTextKeys", "FunctionChanges", "Functions",
                "KeyDisables", "MaskChanges", "PeriodDrops", "ProcedureChanges", "Procedures", "Synonyms", "TableChanges", "Tables", "Triggers",
                "UnreadDeployments", "UserTypes", "Views",
            ],
            records);
    }
}
