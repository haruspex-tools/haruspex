using System.Text;
using Haruspex.Cli;
using Haruspex.Ir;

namespace Haruspex.Tests.Config;

/// <summary>
/// The config file applied to inferred IR: overrides as transformations of the document, then naming. Each error and
/// warning is checked word for word, since the message is how a user finds what to change.
/// </summary>
public partial class ConfigApplicationTests
{
    private const string Tables = """
        CREATE SCHEMA sales;
        GO
        CREATE TABLE sales.customer
        (
            customer_id int NOT NULL PRIMARY KEY,
            name nvarchar(40) NULL,
            status varchar(8) COLLATE Latin1_General_100_CI_AS NOT NULL CHECK (status IN ('open', 'closed')),
            level int NOT NULL CHECK (level IN (1, 2)),
            notes json NULL
        );
        GO
        CREATE TABLE sales.line (line_id int NOT NULL PRIMARY KEY, customer_id int NOT NULL, sku varchar(10) NOT NULL);
        GO
        """;

    /// <summary>Customers as JSON: a nullable name, a collation set, an exact set, JSON text and nested lines.</summary>
    private const string GetCustomer = """
        CREATE PROCEDURE sales.get_customer AS
        SELECT c.customer_id AS id, c.name, c.status, c.level, c.notes,
               (SELECT l.sku FROM sales.line AS l WHERE l.customer_id = c.customer_id FOR JSON PATH) AS lines
        FROM sales.customer AS c
        FOR JSON PATH;
        GO
        """ + "\n";

    private sealed record Run(ConfigApplied Applied)
    {
        public IrDocument Document => Applied.Document ?? throw new InvalidOperationException(string.Join("\n", Applied.Problems));

        public IReadOnlyList<string> Problems => [.. Applied.Problems.Select(p => p.ToString())];

        public string Error => Assert.Single(Applied.Problems, p => p.Severity == ConfigSeverity.Error).ToString();
    }

    private static Run Apply(string procedures, string config, string include = "*.*", string? output = null, IReadOnlyCollection<string>? selectedByFile = null)
    {
        using var dir = new TempDirectory();
        dir.File("tables.sql", Tables);
        dir.File("procedures.sql", procedures);
        var project = SqlProject.Load(dir.Path);
        Assert.Empty(project.Diagnostics);

        var read = ConfigReader.Read(Encoding.UTF8.GetBytes(config), "haruspex.json", dir.Path);
        Assert.Empty(read.Problems);
        var (selected, _) = ProcedureSelection.Select(project.Procedures, [ObjectPattern.Parse(include)], read.Config!);
        var byFile = selectedByFile?.Select(t => project.Procedures.Single(p => p.ToString() == t)).ToList();
        return new Run(project.Infer(selected, read.Config!, byFile, output is null ? null : Path.Combine(dir.Path, output)));
    }

    private static IrObjectType Type(IrDocument doc, string name) => doc.Types[name];

    private static IrField Field(IrDocument doc, string type, string name) => doc.Types[type].Fields.Single(f => f.Name == name);
}
