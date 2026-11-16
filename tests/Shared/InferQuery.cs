using Haruspex.Cli;
using Haruspex.Diagnostics;
using Haruspex.Emit.Zod;
using Haruspex.Ir;
using Haruspex.SqlServer.Inference;
using Haruspex.SqlServer.Sources;

namespace Haruspex.TestSupport;

/// <summary>
/// Infers a query file from SQL a test writes, against tables it writes too: how a unit test of query files states its input.
/// The document is inference's own, before the config file's names are settled, so a query's name is its path.
/// </summary>
internal static class InferQuery
{
    /// <summary>A shop's invented tables, procedure and functions, which the tests of query files read their queries against: here and in the observations of the server.</summary>
    public const string Shop = """
        CREATE TABLE shop.customers (
            customer_id int NOT NULL PRIMARY KEY, name nvarchar(80) NOT NULL, email varchar(120) NULL, tier char(1) NOT NULL,
            balance decimal(10,2) NOT NULL, joined date NOT NULL, code nchar(4) NULL, flags tinyint NOT NULL, seen datetime2(3) NULL,
            notes nvarchar(max) NULL, rate float NOT NULL, credit money NOT NULL, stamp datetime NULL, token varbinary(16) NULL,
            active bit NOT NULL);
        GO
        CREATE TABLE shop.orders (order_id int NOT NULL PRIMARY KEY, customer_id int NOT NULL, placed datetime2(0) NOT NULL, total decimal(12,2) NOT NULL, qty smallint NOT NULL);
        GO
        CREATE PROCEDURE shop.touch @customer_id int, @why nvarchar(20) = NULL AS SELECT 1 AS one;
        GO
        CREATE FUNCTION shop.discount (@total decimal(12,2), @rate decimal(4,3)) RETURNS decimal(12,2) AS BEGIN RETURN @total * @rate; END;
        GO
        CREATE FUNCTION shop.orders_of (@customer_id bigint) RETURNS TABLE AS RETURN SELECT o.order_id FROM shop.orders AS o WHERE o.customer_id = @customer_id;
        """;

    /// <summary>
    /// The document of the query file <paramref name="path"/> holding <paramref name="query"/>, read against a folder of scripts
    /// holding <paramref name="schema"/>; the test fails on any problem reading either.
    /// </summary>
    public static IrDocument Infer(string schema, string query, string path = "queries/q.sql") => Inferred(schema, query, path).Expected();

    /// <summary>As <c>Infer</c>, with what reading the query file and inferring it found.</summary>
    public static Outcome<IrDocument> Inferred(string schema, string query, string path = "queries/q.sql")
    {
        using var dir = new TempDirectory();
        dir.File("schema.sql", schema);
        var read = SqlScriptFolder.Read(dir.Path);
        Assert.True(read.Diagnostics.Count == 0, string.Join("\n", read.Diagnostics));
        return QueryFileReader.Parse(query.ReplaceLineEndings("\n"), path)
            .Then(file => IrInference.Infer(read.Model, [], queries: [file]));
    }

    /// <summary>
    /// The module of the query files <paramref name="queries"/> (each a path below <c>sql/</c> and its text), read against a folder
    /// holding <paramref name="schema"/>, as <c>haruspex generate</c> makes it with a config file of <c>"queries": "sql"</c>: each
    /// named from its file. The test fails on any problem.
    /// </summary>
    public static IrDocument Module(string schema, params (string Path, string Sql)[] queries)
    {
        using var dir = new TempDirectory();
        dir.File("db/schema.sql", schema);
        foreach (var (path, sql) in queries)
            dir.File(path, sql);
        dir.File(HaruspexConfig.FileName, "{ \"source\": \"db\", \"out\": \"out\", \"queries\": \"sql\" }");
        var config = ConfigReader.Read(Path.Combine(dir.Path, HaruspexConfig.FileName), new ReservedWords(TypeScriptNames.Language, TypeScriptNames.IsReserved)).Expected();
        var files = QuerySelection.Select(config).Expected().Select(f => QueryFileReader.Read(f.Path, f.Relative).Expected()).ToList();
        var project = SqlProject.Load(Path.Combine(dir.Path, "db"));
        Assert.True(project.Diagnostics.Count == 0, string.Join("\n", project.Diagnostics));
        return project.WithQueries(dir.Path, files).Infer([], config).Expected();
    }

    /// <summary>The query's one object.</summary>
    public static IrObject Query(this IrDocument document) => Assert.Single(document.Objects, o => o.Kind == IrObjectKind.Query);

    /// <summary>Each of the query's parameters with its type's SQL, or <c>unknown</c>, as <c>@name type</c>.</summary>
    public static IReadOnlyList<string> Parameters(this IrDocument document) =>
        [.. document.Query().Parameters.Select(p => p.Name + " " + (p.Type is IrScalar { Sql: { } sql } ? sql : "unknown"))];
}
