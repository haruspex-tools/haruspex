using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;
using Haruspex.SqlServer.Sources;

namespace Haruspex.Tests.Semantics;

/// <summary>
/// Semantics' reading of the schema: what types, alias types and CHECK constraints guarantee about a column's
/// values. Each form that is read has a test, and so does each form that must be ignored, because ignoring is what
/// keeps a constraint the reader doesn't understand from narrowing a type wrongly.
/// </summary>
public partial class SchemaFactsTests
{
    private static DatabaseModel Model(string sql)
    {
        using var dir = new TempDirectory();
        dir.File("schema.sql", sql);
        var read = SqlScriptFolder.Read(dir.Path);
        Assert.Empty(read.Diagnostics);
        return read.Model;
    }

    /// <summary>The facts about column <paramref name="column"/> of <c>dbo.t</c>.</summary>
    private static ColumnFacts Facts(string sql, string column = "c", string schema = "dbo")
    {
        var model = Model(sql);
        var table = model.FindTable(new ObjectName(schema, "t"))!;
        return SchemaRules.ColumnFacts(model, table, table.Column(column)!);
    }

    /// <summary>The facts about <c>c</c>, declared as <paramref name="definition"/>, with each check in <paramref name="checks"/> on the table.</summary>
    private static ColumnFacts Column(string definition, params string[] checks) =>
        Facts($"CREATE TABLE dbo.t (id int NOT NULL PRIMARY KEY, c {definition}{string.Concat(checks.Select(k => ", " + k))});");

    private static bool NeverNull(string sql, string column = "c")
    {
        var model = Model(sql);
        var table = model.FindTable(new ObjectName("dbo", "t"))!;
        return SchemaRules.IsNeverNull(model, table, table.Column(column)!);
    }

    private static string Bound(BoundFact? bound) => bound is null ? "none" : (bound.Inclusive ? "" : "exclusive ") + bound.Value;

    private static string Source(FactSource? from) => from switch
    {
        TypeSource t => "type " + t.Type,
        CheckSource c => "check " + c.Check,
        null => "none",
        _ => throw new InvalidOperationException(),
    };
}
