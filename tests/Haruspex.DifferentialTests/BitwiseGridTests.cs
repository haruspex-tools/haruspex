using System.Globalization;
using Haruspex.Cli;
using Haruspex.DifferentialTests.Server;
using Haruspex.Ir;

namespace Haruspex.DifferentialTests;

/// <summary>
/// The types haruspex gives the bitwise operators, the shifts and <c>||</c> are the ones SQL Server describes, over every pair of
/// a grid of types, and where the server refuses a pair, haruspex leaves it unknown. The rules are in
/// <c>Typing/Bitwise.cs</c> (Haruspex.SqlServer.Inference's README.md, "Bitwise operators, shifts and the concatenation operator"); this checks them through the
/// whole pipeline: a procedure is inferred from SQL and each column's type compared with the server's description of the same
/// expression, or its refusal. Binary types are left out, since haruspex doesn't model them.
/// </summary>
public class BitwiseGridTests(SqlServerFixture server)
{
    /// <summary>The operand columns: name and type.</summary>
    private static readonly (string Name, string Type)[] Columns =
    [
        ("ti", "tinyint"), ("si", "smallint"), ("i", "int"), ("bi", "bigint"), ("bt", "bit"), ("d52", "decimal(5,2)"), ("n380", "numeric(38,0)"),
        ("m", "money"), ("sm", "smallmoney"), ("f", "float"), ("r", "real"), ("c3", "char(3)"), ("v10", "varchar(10)"), ("nc2", "nchar(2)"),
        ("nv10", "nvarchar(10)"), ("v8k", "varchar(8000)"), ("nv4k", "nvarchar(4000)"), ("vm", "varchar(max)"), ("nvm", "nvarchar(max)"),
        ("dd", "date"), ("t3", "time(3)"), ("dt", "datetime"), ("dt2", "datetime2(0)"), ("dto", "datetimeoffset"), ("g", "uniqueidentifier"),
    ];

    private static readonly string[] Operators = ["&", "|", "^", "<<", ">>", "||"];

    private static List<string> Expressions() =>
    [
        .. Operators.SelectMany(op => Columns.SelectMany(a => Columns.Select(b => $"{a.Name} {op} {b.Name}"))),
        .. Columns.Select(a => $"~{a.Name}"),
    ];

    [Fact]
    public async Task Each_operator_has_the_type_sql_server_describes_or_is_unknown_where_it_refuses()
    {
        await server.RequireAsync();
        var expressions = Expressions();
        var inferred = Infer(expressions);
        var described = await Describe(expressions);

        var differences = expressions.Select((e, i) => (e, Name: Name(i)))
            .Where(x => !string.Equals(inferred.GetValueOrDefault(x.Name), described.GetValueOrDefault(x.Name), StringComparison.OrdinalIgnoreCase))
            .Select(x => $"{x.e}: haruspex {inferred.GetValueOrDefault(x.Name) ?? "(unknown)"}, SQL Server {described.GetValueOrDefault(x.Name) ?? "(refused)"}")
            .ToList();
        Assert.True(differences.Count == 0, $"{differences.Count} of {expressions.Count} differ:\n" + string.Join("\n", differences));

        // The grid holds both: pairs typed and pairs refused.
        Assert.InRange(described.Count, 1, expressions.Count - 1);
    }

    private static string Name(int i) => $"e{i.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// The server's description of each expression over one row of the columns, by name; a refused one is missing. One query
    /// each, in one batch, since a refused expression fails the whole of a query (and INSERT … VALUES takes at most 1,000 rows,
    /// a derived table any number).
    /// </summary>
    private async Task<Dictionary<string, string>> Describe(List<string> expressions)
    {
        var row = "(SELECT " + string.Join(", ", Columns.Select(c => $"CAST(NULL AS {c.Type}) AS {c.Name}")) + ") AS g";
        var values = string.Join(",\n", expressions.Select((e, i) => $"(N'{Name(i)}', N'SELECT {e.Replace("'", "''", StringComparison.Ordinal)} AS x FROM {row}')"));
        var outcome = await ObservedCall.Run(server, [], $"""
            SET NOCOUNT ON;
            DECLARE @q TABLE (name sysname NOT NULL, query nvarchar(max) NOT NULL);
            INSERT @q SELECT v.name, v.query FROM (VALUES {values}) AS v (name, query);
            DECLARE @out TABLE (name sysname NOT NULL, type nvarchar(200) NOT NULL);
            DECLARE @name sysname, @query nvarchar(max);
            DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT name, query FROM @q;
            OPEN c;
            FETCH NEXT FROM c INTO @name, @query;
            WHILE @@FETCH_STATUS = 0
            BEGIN
                INSERT @out SELECT @name, d.system_type_name FROM sys.dm_exec_describe_first_result_set(@query, NULL, 0) AS d WHERE d.column_ordinal = 1 AND d.system_type_name IS NOT NULL;
                FETCH NEXT FROM c INTO @name, @query;
            END;
            SELECT o.name + ' ' + o.type AS c FROM @out AS o;
            """);
        Assert.Null(outcome.Error);
        return outcome.Sets[0]["c: ".Length..].Split(';').ToDictionary(t => t[..t.IndexOf(' ', StringComparison.Ordinal)], t => t[(t.IndexOf(' ', StringComparison.Ordinal) + 1)..], StringComparer.Ordinal);
    }

    /// <summary>The SQL type haruspex infers for each expression, by name; one it leaves unknown is missing.</summary>
    private static Dictionary<string, string> Infer(List<string> expressions)
    {
        var dir = Directory.CreateTempSubdirectory("haruspex-grid-");
        try
        {
            var columns = string.Join(", ", Columns.Select(c => $"{c.Name} {c.Type} NOT NULL"));
            File.WriteAllText(Path.Join(dir.FullName, "tables.sql"), $"CREATE TABLE dbo.g (id int NOT NULL PRIMARY KEY, {columns});\n");
            File.WriteAllText(Path.Join(dir.FullName, "p.sql"), $"CREATE PROCEDURE dbo.p AS\nSELECT {string.Join(",\n       ", expressions.Select((e, i) => $"{e} AS {Name(i)}"))} FROM dbo.g;\n");

            var project = SqlProject.Load(dir.FullName);
            Assert.Empty(project.Diagnostics);
            var document = project.Infer(project.Procedures).Expected();
            var row = Assert.IsType<IrRef>(Assert.IsType<IrArray>(Assert.Single(Assert.Single(document.Objects).Results!).Type).Items);
            return document.Types[row.Name].Fields
                .Where(f => f.Type is IrScalar { Sql: not null })
                .ToDictionary(f => f.Name, f => ((IrScalar)f.Type).Sql!, StringComparer.Ordinal);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
