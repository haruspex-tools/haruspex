using System.Globalization;
using Haruspex.Cli;
using Haruspex.DifferentialTests.Server;
using Haruspex.Ir;

namespace Haruspex.DifferentialTests;

/// <summary>
/// Where haruspex says a decimal may lose precision (<c>precision-loss</c>) is where the server rounds it to NULL, over a grid
/// of operand types that reaches the edges of the decimal rules (38 digits, all places, a scale of 6), with integers, bit,
/// money, smallmoney and literals among them: every operator over every pair, and every conversion into a decimal. Each
/// expression is inferred from SQL through the whole pipeline, and run over rows of zeros, small values, each type's greatest
/// and a one followed by zeros in every integer digit, every row of one beside every row of the other, with ANSI_WARNINGS and
/// ARITHABORT off and NUMERIC_ROUNDABORT on, then off. It loses precision when some row is NULL with it on and a value with it
/// off: an overflow or a division by zero is NULL under both. haruspex must give the reason exactly then: for a product, a
/// quotient and a conversion the types decide, and the zeros already show it; for a sum, only a result with as many integer
/// digits as its type keeps rounds, which the row of a one and zeros reaches without overflowing; for a string, only one with
/// more places than the target, which its last row has. An expression no row gives a value for (every value overflows or
/// divides by zero, as decimal(38,38) / bit does) shows nothing either way, and is left out. When the rule was written it was
/// also checked over several thousand random pairs and values, which is too slow to keep as a test (Typing/Arithmetic.cs,
/// <c>Arithmetic.Rounds</c>).
/// </summary>
public class PrecisionLossGridTests(SqlServerFixture server)
{
    /// <summary>The operand columns: name and type.</summary>
    private static readonly (string Name, string Type)[] Columns =
    [
        ("d10", "decimal(1,0)"), ("d52", "decimal(5,2)"), ("d55", "decimal(5,5)"), ("d102", "decimal(10,2)"), ("d192", "decimal(19,2)"),
        ("d196", "decimal(19,6)"), ("d2810", "decimal(28,10)"), ("d3018", "decimal(30,18)"), ("d330", "decimal(33,0)"),
        ("d380", "decimal(38,0)"), ("d3810", "decimal(38,10)"), ("d3838", "decimal(38,38)"), ("n104", "numeric(10,4)"),
        ("ti", "tinyint"), ("i", "int"), ("bi", "bigint"), ("bt", "bit"), ("m", "money"), ("sm", "smallmoney"),
    ];

    /// <summary>Literals beside a column: an integer counts its own digits, a decimal its own digits and places.</summary>
    private static readonly string[] Literals = ["3", "1.5", "0.0001"];

    /// <summary>The decimal types each column is converted to.</summary>
    private static readonly string[] Targets = ["decimal(5,2)", "decimal(10,0)", "decimal(10,4)", "decimal(19,4)", "decimal(38,0)", "decimal(38,10)", "decimal(38,38)"];

    /// <summary>A string column whose values have 0, 1 and 4 places, converted to decimals of 2 places and of 6.</summary>
    private static readonly string[] Strings = ["CAST(x.s20 AS decimal(10,2))", "CAST(x.s20 AS decimal(20,6))", "CAST('1.20' AS decimal(10,1))", "CAST('1.2' AS decimal(10,1))"];

    /// <summary>The string column's value in each row.</summary>
    private static readonly string[] StringValues = ["0", "1.5", "12.3456", "0.123456789012345678"];

    /// <summary>How many rows the table has; each expression reads every pair of them.</summary>
    private const int Rows = 4;

    private static IEnumerable<string> Expressions()
    {
        foreach (var op in new[] { "+", "-", "*", "/", "%" })
        {
            foreach (var (a, ta) in Columns)
            {
                foreach (var (b, tb) in Columns.Where(c => ta.Contains('(', StringComparison.Ordinal) || c.Type.Contains('(', StringComparison.Ordinal)))
                    yield return $"x.{a} {op} y.{b}";
                foreach (var literal in Literals.Where(_ => ta.Contains('(', StringComparison.Ordinal)))
                {
                    yield return $"x.{a} {op} {literal}";
                    yield return $"{literal} {op} y.{a}";
                }
            }
        }

        // money or smallmoney into decimal(38,38), which can't hold 1, is an error (234, 292) even under both options off, and
        // would end the query: haruspex claims an overflow there, which is less than holds.
        foreach (var (a, ta) in Columns)
        {
            foreach (var target in Targets.Where(t => !(ta is "money" or "smallmoney" && t == "decimal(38,38)")))
                yield return $"CAST(x.{a} AS {target})";
        }

        foreach (var s in Strings)
            yield return s;
    }

    /// <summary>A column's value in each of the rows: zero, a small value, the type's greatest, a one and zeros.</summary>
    private static string[] Values(string type)
    {
        if (type == "bit")
            return ["0", "1", "1", "1"];
        var (precision, scale) = type switch
        {
            "tinyint" => (3, 0),
            "int" => (10, 0),
            "bigint" => (19, 0),
            "money" => (19, 4),
            "smallmoney" => (10, 4),
            _ => (int.Parse(type[(type.IndexOf('(', StringComparison.Ordinal) + 1)..type.IndexOf(',', StringComparison.Ordinal)], CultureInfo.InvariantCulture),
                  int.Parse(type[(type.IndexOf(',', StringComparison.Ordinal) + 1)..^1], CultureInfo.InvariantCulture)),
        };
        var greatest = type switch
        {
            "tinyint" => "255",
            "int" => "2147483647",
            "bigint" => "9223372036854775807",
            "money" => "922337203685477.5807",
            "smallmoney" => "214748.3647",
            _ => (precision > scale ? new string('9', precision - scale) : "0") + (scale > 0 ? "." + new string('9', scale) : ""),
        };
        var digits = type switch
        {
            "tinyint" => 3,
            "int" => 10,
            "smallmoney" => 6,
            "bigint" => 19,
            "money" => 15,
            _ => precision - scale,
        };
        return ["0", precision > scale ? "1" : "0.5", greatest, digits > 0 ? "1" + new string('0', digits - 1) : "0.1"];
    }

    [Fact]
    public async Task Haruspex_gives_precision_loss_exactly_where_the_server_rounds_to_null()
    {
        server.Require();
        var expressions = Expressions().ToList();
        var inferred = Infer(expressions);

        // Four rows, k = 0 to 3: zeros, small values, the greatest, a one and zeros; each expression reads one row as x and one
        // as y.
        var columns = string.Join(", ", Columns.Select(c => $"{c.Name} {c.Type} NOT NULL"));
        // An INSERT each: one VALUES list of the three would bring each column's literals to one type first, which overflows.
        var rows = string.Concat(Enumerable.Range(0, Rows).Select(k =>
            $" INSERT dbo.g VALUES ({k.ToString(CultureInfo.InvariantCulture)}, {string.Join(", ", Columns.Select(c => Values(c.Type)[k]))}, '{StringValues[k]}');"));
        string[] setup = [$"CREATE TABLE dbo.g (k int NOT NULL PRIMARY KEY, {columns}, s20 varchar(20) NOT NULL);{rows}"];
        var select = "SELECT " + string.Join(", ", expressions.Select((e, i) => $"CASE WHEN {e} IS NULL THEN 'N' ELSE 'V' END AS e{i.ToString(CultureInfo.InvariantCulture)}"))
            + " FROM dbo.g AS x CROSS JOIN dbo.g AS y ORDER BY x.k, y.k;";

        var on = await Outcomes(setup, "ON", select);
        var off = await Outcomes(setup, "OFF", select);

        var differences = expressions.Select((e, i) => (Expression: e, Name: $"e{i.ToString(CultureInfo.InvariantCulture)}", Index: i))
            .Where(x => inferred.ContainsKey(x.Name) && off.Any(r => r[x.Index] == 'V'))
            .Select(x => (x.Expression, Claimed: inferred[x.Name], Lost: on.Zip(off).Any(r => r.First[x.Index] == 'N' && r.Second[x.Index] == 'V')))
            .Where(x => x.Claimed != x.Lost)
            .Select(x => $"{x.Expression}: haruspex {(x.Claimed ? "gives" : "doesn't give")} precision-loss, and the server {(x.Lost ? "rounds it to NULL" : "never does")}")
            .ToList();
        Assert.True(differences.Count == 0, $"{differences.Count} of {expressions.Count} differ:\n" + string.Join("\n", differences));
        Assert.True(inferred.Count > expressions.Count * 9 / 10, $"Only {inferred.Count} of {expressions.Count} were typed.");
    }

    /// <summary>Each row's outcome, one character an expression ('N' or 'V'), under ANSI_WARNINGS and ARITHABORT off.</summary>
    private async Task<List<string>> Outcomes(string[] setup, string numericRoundAbort, string select)
    {
        var outcome = await ObservedCall.Run(server, setup, $"SET ANSI_WARNINGS OFF; SET ARITHABORT OFF; SET NUMERIC_ROUNDABORT {numericRoundAbort};\n{select}\nSET NUMERIC_ROUNDABORT OFF; SET ANSI_WARNINGS ON; SET ARITHABORT ON;");
        Assert.Null(outcome.Error);
        var set = outcome.Sets[0];
        var rows = set[(set.IndexOf(": ", StringComparison.Ordinal) + 2)..].Split(';');
        Assert.Equal(Rows * Rows, rows.Length);
        return [.. rows.Select(r => string.Concat(r.Split(',')))];
    }

    /// <summary>Whether haruspex gives each column <c>precision-loss</c>, by name; a column it leaves unknown is missing.</summary>
    private static Dictionary<string, bool> Infer(List<string> expressions)
    {
        var dir = Directory.CreateTempSubdirectory("haruspex-precision-grid-");
        try
        {
            var columns = string.Join(", ", Columns.Select(c => $"{c.Name} {c.Type} NOT NULL"));
            File.WriteAllText(Path.Join(dir.FullName, "tables.sql"), $"CREATE TABLE dbo.g (k int NOT NULL PRIMARY KEY, {columns}, s20 varchar(20) NOT NULL);\n");
            File.WriteAllText(Path.Join(dir.FullName, "p.sql"), "CREATE PROCEDURE dbo.p AS\nSELECT "
                + string.Join(",\n       ", expressions.Select((e, i) => $"{e} AS e{i.ToString(CultureInfo.InvariantCulture)}"))
                + "\nFROM dbo.g AS x CROSS JOIN dbo.g AS y;\n");

            var project = SqlProject.Load(dir.FullName);
            Assert.Empty(project.Diagnostics);
            var document = project.Infer(project.Procedures).Expected();
            var claims = new Dictionary<string, bool>(StringComparer.Ordinal);
            if (document.Objects is [{ Results: [{ Type: IrArray { Items: IrRef row } }] }])
            {
                foreach (var field in document.Types[row.Name].Fields.Where(f => f.Type is IrScalar))
                    claims[field.Name] = field.NullableBecause.Any(r => r.Rule == IrNullRule.PrecisionLoss);
            }

            return claims;
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
