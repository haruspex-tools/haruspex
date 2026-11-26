using System.Globalization;
using Haruspex.Cli;
using Haruspex.DifferentialTests.Server;
using Haruspex.Ir;

namespace Haruspex.DifferentialTests;

/// <summary>
/// The types haruspex gives arithmetic and the numeric functions are the ones SQL Server describes, over a grid of operand
/// types that reaches the edges of the decimal rules (38 digits, all places, the cap's thresholds of 32 integer digits and a
/// scale of 6), with integers, bit, money, smallmoney and literals among them. The precision rules are Microsoft's, cut past 38
/// digits (Haruspex.SqlServer.Inference's README.md, "Decimal arithmetic"); this checks them through the whole pipeline: a
/// procedure is inferred from SQL and each column's type compared with the server's description of the same expression.
/// When the rules were written they were also checked over 144,500 decimal pairs, which is too slow to keep as a test.
/// </summary>
public class ArithmeticGridTests(SqlServerFixture server)
{
    /// <summary>The operand columns: name and type.</summary>
    private static readonly (string Name, string Type)[] Columns =
    [
        ("d10", "decimal(1,0)"), ("d52", "decimal(5,2)"), ("d55", "decimal(5,5)"), ("d102", "decimal(10,2)"), ("d192", "decimal(19,2)"),
        ("d196", "decimal(19,6)"), ("d2810", "decimal(28,10)"), ("d3018", "decimal(30,18)"), ("d326", "decimal(32,6)"), ("d330", "decimal(33,0)"),
        ("d380", "decimal(38,0)"), ("d3810", "decimal(38,10)"), ("d3838", "decimal(38,38)"), ("n104", "numeric(10,4)"), ("n3837", "numeric(38,37)"),
        ("ti", "tinyint"), ("si", "smallint"), ("i", "int"), ("bi", "bigint"), ("bt", "bit"), ("m", "money"), ("sm", "smallmoney"),
    ];

    /// <summary>Literals as operands, beside a column: a decimal literal is numeric of its digits, an integer literal decimal of them.</summary>
    private static readonly string[] Literals = ["1.0", "-1.0", "2", "100", "0.05", "12345678901234567890"];

    private static IEnumerable<string> BinaryExpressions()
    {
        foreach (var op in new[] { "+", "-", "*", "/", "%" })
        {
            foreach (var (a, ta) in Columns)
            {
                foreach (var (b, tb) in Columns)
                {
                    // Two bits are an error; integers, money and bit alone are not the decimal rules this grid is about, but
                    // a few stay, beside each other, to keep their precedence in view.
                    if ((ta == "bit" && tb == "bit") || (!ta.Contains('(', StringComparison.Ordinal) && !tb.Contains('(', StringComparison.Ordinal) && a != "i" && b != "i"))
                        continue;
                    yield return $"{a} {op} {b}";
                }

                foreach (var literal in Literals)
                {
                    yield return $"{a} {op} {literal}";
                    yield return $"{literal} {op} {a}";
                }
            }
        }
    }

    private static IEnumerable<string> FunctionExpressions()
    {
        foreach (var (a, _) in Columns)
        {
            yield return $"-{a}";
            yield return $"ROUND({a}, 1)";
            yield return $"ROUND({a}, -1, 1)";
            yield return $"FLOOR({a})";
            yield return $"CEILING({a})";
            yield return $"ABS({a})";
            yield return $"SIGN({a})";
            yield return $"POWER({a}, 2)";
            yield return $"SQRT({a})";
            yield return $"SQUARE({a})";
            yield return $"EXP({a})";
            yield return $"LOG({a})";
            yield return $"LOG10({a})";
        }
    }

    private static IEnumerable<string> AggregateExpressions()
    {
        foreach (var (a, t) in Columns.Where(c => c.Type != "bit"))
        {
            yield return $"AVG({a})";
            yield return $"AVG({a}) OVER ()";
            yield return $"STDEV({a})";
            yield return $"VARP({a}) OVER ()";
        }
    }

    public static TheoryData<string> Grids { get; } = ["binary", "functions", "aggregates"];

    [Theory]
    [MemberData(nameof(Grids))]
    public async Task Each_expression_has_the_type_sql_server_describes(string grid)
    {
        await server.RequireAsync();
        var expressions = grid switch
        {
            "binary" => BinaryExpressions().ToList(),
            "functions" => [.. FunctionExpressions().Where(e => !e.StartsWith("-bt", StringComparison.Ordinal))],
            _ => [.. AggregateExpressions()],
        };

        // An aggregate and a window cannot share a select list without GROUP BY, so each aggregate is read in a query of its own.
        var oneEach = grid == "aggregates";
        var inferred = Infer(expressions, oneEach);

        var described = new Dictionary<string, string>(StringComparer.Ordinal);
        var row = "(SELECT " + string.Join(", ", Columns.Select(c => $"CAST(1 AS {c.Type}) AS {c.Name}")) + ") AS g";
        foreach (var (query, names) in Queries(expressions, row, oneEach))
        {
            var outcome = await ObservedCall.Run(server, [], $"SELECT name + ' ' + system_type_name AS c FROM sys.dm_exec_describe_first_result_set(N'{query.Replace("'", "''", StringComparison.Ordinal)}', NULL, 0) ORDER BY column_ordinal;");
            Assert.Null(outcome.Error);
            var types = outcome.Sets[0]["c: ".Length..].Split(';');
            Assert.Equal(names.Count, types.Length);
            foreach (var t in types)
                described[t[..t.IndexOf(' ', StringComparison.Ordinal)]] = t[(t.IndexOf(' ', StringComparison.Ordinal) + 1)..];
        }

        var differences = expressions.Select((e, i) => (e, Name: $"e{i.ToString(CultureInfo.InvariantCulture)}"))
            .Where(x => !string.Equals(inferred.GetValueOrDefault(x.Name), described.GetValueOrDefault(x.Name), StringComparison.OrdinalIgnoreCase))
            .Select(x => $"{x.e}: haruspex {inferred.GetValueOrDefault(x.Name) ?? "(unknown)"}, SQL Server {described.GetValueOrDefault(x.Name) ?? "(an error)"}")
            .ToList();
        Assert.True(differences.Count == 0, $"{differences.Count} of {expressions.Count} differ:\n" + string.Join("\n", differences));
    }

    /// <summary>The server's queries: one select list, or for aggregates one query each (an aggregate needs no GROUP BY).</summary>
    private static IEnumerable<(string Query, List<string> Names)> Queries(List<string> expressions, string row, bool oneEach)
    {
        if (!oneEach)
        {
            yield return ($"SELECT {string.Join(", ", expressions.Select((e, i) => $"{e} AS e{i.ToString(CultureInfo.InvariantCulture)}"))} FROM {row}", [.. expressions.Select((_, i) => $"e{i.ToString(CultureInfo.InvariantCulture)}")]);
            yield break;
        }

        for (var i = 0; i < expressions.Count; i++)
            yield return ($"SELECT {expressions[i]} AS e{i.ToString(CultureInfo.InvariantCulture)} FROM {row}", [$"e{i.ToString(CultureInfo.InvariantCulture)}"]);
    }

    /// <summary>The SQL type haruspex infers for each column, by name; a column it leaves unknown is missing.</summary>
    private static Dictionary<string, string> Infer(List<string> expressions, bool oneEach)
    {
        var dir = Directory.CreateTempSubdirectory("haruspex-grid-");
        try
        {
            var columns = string.Join(", ", Columns.Select(c => $"{c.Name} {c.Type} NOT NULL"));
            File.WriteAllText(Path.Join(dir.FullName, "tables.sql"), $"CREATE TABLE dbo.g (id int NOT NULL PRIMARY KEY, {columns});\n");
            static string Item(string e, int i) => $"{e} AS e{i.ToString(CultureInfo.InvariantCulture)}";
            var bodies = oneEach
                ? expressions.Select((e, i) => $"CREATE PROCEDURE dbo.p{i.ToString(CultureInfo.InvariantCulture)} AS\nSELECT {Item(e, i)} FROM dbo.g;\n")
                : [$"CREATE PROCEDURE dbo.p0 AS\nSELECT {string.Join(",\n       ", expressions.Select(Item))} FROM dbo.g;\n"];
            var n = 0;
            foreach (var body in bodies)
                File.WriteAllText(Path.Join(dir.FullName, $"p{n++.ToString(CultureInfo.InvariantCulture)}.sql"), body);

            var project = SqlProject.Load(dir.FullName);
            Assert.Empty(project.Diagnostics);
            var document = project.Infer(project.Procedures).Expected();
            var types = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var procedure in document.Objects)
            {
                if (procedure.Results is not [{ Type: IrArray { Items: IrRef row } }])
                    continue;
                foreach (var field in document.Types[row.Name].Fields)
                {
                    if (field.Type is IrScalar { Sql: { } sql })
                        types[field.Name] = sql;
                }
            }

            return types;
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
