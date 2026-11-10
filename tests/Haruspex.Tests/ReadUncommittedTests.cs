using Haruspex.Cli;
using Haruspex.Ir;

namespace Haruspex.Tests;

/// <summary>
/// Visible read-uncommitted (slice 6f, decided 10 Nov 2026): a <c>NOLOCK</c> or <c>READUNCOMMITTED</c> hint on a source, or
/// <c>SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED</c> earlier in the procedure or in a caller, drops the key-based "at most
/// one row" claims through that source, because a scan under read-uncommitted can return a row twice. How far the isolation
/// level reaches is observed in the differential tests' <c>ReadUncommittedObservationTests</c>.
/// </summary>
public class ReadUncommittedTests
{
    private const string Tables = """
        CREATE TABLE dbo.t (id int NOT NULL PRIMARY KEY, name nvarchar(10) NULL);
        GO
        CREATE TABLE dbo.u (id int NOT NULL PRIMARY KEY, t_id int NOT NULL, note nvarchar(10) NULL);
        GO
        CREATE VIEW dbo.tv AS SELECT t.id, t.name FROM dbo.t AS t;
        GO
        CREATE VIEW dbo.tv_dirty AS SELECT t.id, t.name FROM dbo.t AS t WITH (NOLOCK);
        GO
        """;

    private static IrDocument Infer(string procedures, string name = "dbo.p")
    {
        using var dir = new TempDirectory();
        dir.File("tables.sql", Tables);
        dir.File("p.sql", procedures);
        var project = SqlProject.Load(dir.Path);
        Assert.Empty(project.Diagnostics);
        return project.Infer(project.Select([ObjectPattern.Parse(name)]).Procedures);
    }

    private static IrDocument Procedure(string body) => Infer($"CREATE PROCEDURE dbo.p @id int AS\n{body}");

    private static IrCardinality[] Rows(IrDocument doc) =>
        [.. (doc.Objects[0].Results ?? throw new InvalidOperationException("Unknown results.")).Select(r => r.Cardinality)];

    [Theory]
    [InlineData("WITH (NOLOCK)")]
    [InlineData("(NOLOCK)")]
    [InlineData("WITH (READUNCOMMITTED)")]
    [InlineData("WITH (NOLOCK, INDEX(1))")]
    public void A_key_read_with_nolock_does_not_bound_the_rows(string hint)
    {
        Assert.Equal([IrCardinality.AtMostOne], Rows(Procedure("SELECT t.name FROM dbo.t AS t WHERE t.id = @id;")));
        Assert.Equal([IrCardinality.Many], Rows(Procedure($"SELECT t.name FROM dbo.t AS t {hint} WHERE t.id = @id;")));
    }

    [Fact]
    public void Other_hints_keep_the_key()
    {
        Assert.Equal([IrCardinality.AtMostOne], Rows(Procedure("SELECT t.name FROM dbo.t AS t WITH (READCOMMITTED, ROWLOCK) WHERE t.id = @id;")));
    }

    [Fact]
    public void Only_the_source_read_uncommitted_loses_its_key()
    {
        // u is fixed by its key through the ON, t by @id; with NOLOCK on u alone, u may repeat its row.
        Assert.Equal([IrCardinality.AtMostOne], Rows(Procedure("SELECT t.name, u.note FROM dbo.t AS t JOIN dbo.u AS u ON u.id = t.id WHERE t.id = @id;")));
        Assert.Equal([IrCardinality.Many], Rows(Procedure("SELECT t.name, u.note FROM dbo.t AS t JOIN dbo.u AS u WITH (NOLOCK) ON u.id = t.id WHERE t.id = @id;")));
    }

    [Fact]
    public void An_aggregate_or_top_1_still_bounds_the_rows()
    {
        var doc = Procedure("""
            SELECT COUNT_BIG(*) AS n FROM dbo.t AS t WITH (NOLOCK);
            SELECT TOP (1) t.name FROM dbo.t AS t WITH (NOLOCK) WHERE t.id = @id;
            """);
        Assert.Equal([IrCardinality.One, IrCardinality.AtMostOne], Rows(doc));
    }

    [Fact]
    public void A_view_read_with_nolock_or_reading_with_it_has_no_key()
    {
        Assert.Equal([IrCardinality.AtMostOne], Rows(Procedure("SELECT v.name FROM dbo.tv AS v WHERE v.id = @id;")));
        Assert.Equal([IrCardinality.Many], Rows(Procedure("SELECT v.name FROM dbo.tv AS v WITH (NOLOCK) WHERE v.id = @id;")));
        Assert.Equal([IrCardinality.Many], Rows(Procedure("SELECT v.name FROM dbo.tv_dirty AS v WHERE v.id = @id;")));
    }

    [Fact]
    public void A_subquerys_array_loses_its_bound()
    {
        var doc = Procedure("SELECT t.id, (SELECT u.note FROM dbo.u AS u WITH (NOLOCK) WHERE u.id = t.id FOR JSON PATH) AS notes FROM dbo.t AS t FOR JSON PATH;");
        var row = (IrRef)((IrArray)Assert.Single(doc.Objects[0].Results!).Type).Items;
        var notes = (IrArray)Assert.Single(doc.Types[row.Name].Fields, f => f.Name == "notes").Type;
        Assert.Null(notes.MaxItems);
    }

    [Fact]
    public void The_procedures_isolation_level_applies_to_what_follows_on_any_path()
    {
        var doc = Procedure("""
            SELECT t.name FROM dbo.t AS t WHERE t.id = @id;
            SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
            SELECT t.name FROM dbo.t AS t WHERE t.id = @id;
            SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
            SELECT t.name FROM dbo.t AS t WHERE t.id = @id;
            IF @id > 0 SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
            SELECT t.name FROM dbo.t AS t WHERE t.id = @id;
            """);
        Assert.Equal([IrCardinality.AtMostOne, IrCardinality.Many, IrCardinality.AtMostOne, IrCardinality.Many], Rows(doc));
    }

    [Fact]
    public void A_callee_runs_under_its_callers_isolation_level_and_its_own_ends_when_it_returns()
    {
        // A_procedures_isolation_level_holds_in_the_procedures_it_calls_and_ends_when_it_returns.
        const string Procedures = """
            CREATE PROCEDURE dbo.lookup @id int AS SELECT t.name FROM dbo.t AS t WHERE t.id = @id;
            GO
            CREATE PROCEDURE dbo.dirty_lookup @id int AS SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED; SELECT t.name FROM dbo.t AS t WHERE t.id = @id;
            GO
            CREATE PROCEDURE dbo.p @id int AS
            EXEC dbo.lookup @id = @id;
            EXEC dbo.dirty_lookup @id = @id;
            SELECT t.name FROM dbo.t AS t WHERE t.id = @id;
            SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
            EXEC dbo.lookup @id = @id;
            """;
        Assert.Equal([IrCardinality.AtMostOne, IrCardinality.Many, IrCardinality.AtMostOne, IrCardinality.Many], Rows(Infer(Procedures)));
        Assert.Equal([IrCardinality.AtMostOne], Rows(Infer(Procedures, "dbo.lookup")));
    }
}
