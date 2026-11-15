using System.Globalization;
using Haruspex.Cli;
using Haruspex.Ir;

namespace Haruspex.Tests.Coverage;

/// <summary>
/// Runs procedure bodies through haruspex, each a procedure of its own over a few invented tables (a toy shop), and says which
/// unknowns each gives: how the coverage page knows what haruspex says of each construct and each function, rather than being
/// told. The bodies are the syntax inventory's examples and a call of each function the catalogue lacks.
/// </summary>
internal static class CoverageProbe
{
    /// <summary>The objects the bodies read, each in a file of its own, as a project holds them.</summary>
    /// <remarks>
    /// The edge table has a column because one without any, <c>CREATE TABLE dbo.likes AS EDGE</c>, which SQL Server allows,
    /// stops the reader with an exception, a bug not fixed yet.
    /// </remarks>
    public static readonly (string File, string Sql)[] Schema =
    [
        ("toy.sql", "CREATE TABLE dbo.toy (id int NOT NULL PRIMARY KEY, name nvarchar(40) NOT NULL, price decimal(9,2) NULL, note nvarchar(max) NULL, made datetime2(3) NOT NULL, code varchar(10) NULL, place vector(3) NULL, spec xml NULL);"),
        ("box.sql", "CREATE TABLE dbo.box (id int NOT NULL PRIMARY KEY, toy_id int NOT NULL, qty int NOT NULL DEFAULT 0);"),
        ("rate.sql", "CREATE TABLE dbo.rate (id int NOT NULL PRIMARY KEY, amount int NOT NULL, valid_from datetime2 GENERATED ALWAYS AS ROW START NOT NULL, valid_to datetime2 GENERATED ALWAYS AS ROW END NOT NULL, PERIOD FOR SYSTEM_TIME (valid_from, valid_to)) WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.rate_history));"),
        ("person.sql", "CREATE TABLE dbo.person (id int NOT NULL) AS NODE;"),
        ("likes.sql", "CREATE TABLE dbo.likes (since date NULL) AS EDGE;"),
        ("inbox.sql", "CREATE QUEUE dbo.inbox;"),
        ("ticket.sql", "CREATE SEQUENCE dbo.ticket AS int START WITH 1;"),
        ("by_id.sql", "CREATE PARTITION FUNCTION by_id (int) AS RANGE LEFT FOR VALUES (10);"),
        ("helper.sql", "CREATE PROCEDURE dbo.helper AS SELECT 1 AS one;"),
        ("twice.sql", "CREATE FUNCTION dbo.twice(@n int) RETURNS int AS BEGIN RETURN @n * 2; END"),
        ("rows_of.sql", "CREATE FUNCTION dbo.rows_of() RETURNS TABLE AS RETURN SELECT 1 AS a;"),
    ];

    private const string Prefix = "probe_";

    /// <summary>
    /// Infers each body as a procedure of its own, so that one unknown can't hide another, and gives the reasons of the
    /// unknowns each gives, in the order of <paramref name="bodies"/>.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<IrUnknownReason>> Run(IReadOnlyList<string> bodies)
    {
        using var dir = new TempDirectory();
        foreach (var (file, sql) in Schema)
            dir.File(file, sql);
        for (var n = 0; n < bodies.Count; n++)
            dir.File($"{Prefix}{Number(n)}.sql", $"CREATE PROCEDURE dbo.{Prefix}{Number(n)} AS\n{bodies[n]}\n");

        var project = SqlProject.Load(dir.Path);
        if (project.Diagnostics.Count > 0)
            throw new InvalidOperationException(string.Join("\n", project.Diagnostics));
        var document = project.Infer(project.Procedures).Expected();
        const string Probe = "dbo." + Prefix;
        var ids = document.Objects.Where(o => o.Id.StartsWith(Probe, StringComparison.Ordinal))
            .ToDictionary(o => int.Parse(o.Id[Probe.Length..], CultureInfo.InvariantCulture), o => o.Id);
        if (ids.Count != bodies.Count)
            throw new InvalidOperationException($"{ids.Count} procedures inferred for {bodies.Count} bodies.");
        return [.. Enumerable.Range(0, bodies.Count).Select(n =>
            (IReadOnlyList<IrUnknownReason>)[.. document.Unknowns.Where(u => u.Object == ids[n]).Select(u => u.Reason).Distinct().Order()])];
    }

    private static string Number(int n) => n.ToString(CultureInfo.InvariantCulture);
}
