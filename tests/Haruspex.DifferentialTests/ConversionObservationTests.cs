using System.Globalization;
using Haruspex.DifferentialTests.Server;
using Haruspex.SqlServer.Inference;
using Haruspex.SqlServer.Inference.Typing;
using Haruspex.SqlServer.Semantics;

namespace Haruspex.DifferentialTests;

/// <summary>
/// The table of conversions (<see cref="Conversions.Table"/>), row by row: every conversion it says keeps every value does, at
/// the edges of its types, under the caller's options that would make an overflow NULL.
/// </summary>
public class ConversionObservationTests(SqlServerFixture server)
{
    private static readonly (int Precision, int Scale)[] Digits = [(1, 0), (1, 1), (5, 2), (10, 0), (15, 4), (19, 4), (38, 0), (38, 10), (38, 38)];

    private static readonly int[] Fractions = [0, 3, 7];

    private static readonly int[] Lengths = [1, 3, 6, 11, 20, 36, 41];

    /// <summary>
    /// Each type inference models, with the arguments that reach the rules' edges: lengths about the longest text of each
    /// type, decimals of places only, of none and of 38 digits, fractional seconds of 0, 3 and 7.
    /// </summary>
    private static IEnumerable<ScalarType> Instances(BuiltInType type) => type.Arguments switch
    {
        TypeArgumentKind.Length => [.. Lengths.Append(type.MostLength).Select(n => new ScalarType(type, n)), .. type.IsVarying ? [new ScalarType(type, -1)] : Array.Empty<ScalarType>()],
        TypeArgumentKind.PrecisionAndScale =>
            [.. Digits.Select(d => new ScalarType(type, Precision: d.Precision, Scale: d.Scale))],
        TypeArgumentKind.FractionalSeconds => [.. Fractions.Select(p => new ScalarType(type, Precision: p))],
        _ => [ScalarType.Of(type)],
    };

    /// <summary>The values at a type's edges, as SQL of that type: its least and greatest, or any value where nothing overflows.</summary>
    private static IEnumerable<string> Edges(ScalarType type)
    {
        string Cast(string value) => $"CAST({value} AS {type.Text})";
        string Nines(int n) => n == 0 ? "" : "." + new string('9', n);
        if (type.Type.Least is { } least)
            return [Cast(least.ToString()), Cast(type.Type.Greatest!.Value.ToString())];
        if (type.IsDecimal)
        {
            var largest = ExactDecimal.Largest(type.Precision, type.Scale).ToString();
            return [Cast(largest), Cast("-" + largest)];
        }

        if (type.Type == BuiltInType.Float)
            return [Cast("-1.79E+308"), Cast("1.79E+308")];
        if (type.Type == BuiltInType.Real)
            return [Cast("-3.40E+38"), Cast("3.40E+38")];
        if (type.IsString)
            return [Cast($"REPLICATE(CAST('x' AS {(type.IsNational ? "nvarchar(max)" : "varchar(max)")}), {(type.IsMax ? 9000 : type.Length).ToString(CultureInfo.InvariantCulture)})")];
        if (type.Type == BuiltInType.UniqueIdentifier)
            return [Cast("NEWID()")];
        var fraction = Nines(type.FractionalSeconds);
        return type.Type.Name switch
        {
            "date" => [Cast("'0001-01-01'"), Cast("'9999-12-31'")],
            "time" => [Cast("'00:00:00'"), Cast($"'23:59:59{fraction}'")],
            "datetime" => [Cast("'1753-01-01'"), Cast("'9999-12-31 23:59:59.997'")],
            "smalldatetime" => [Cast("'1900-01-01'"), Cast("'2079-06-06 23:59'")],
            "datetime2" => [Cast("'0001-01-01'"), Cast($"'9999-12-31 23:59:59{fraction}'")],
            "datetimeoffset" => [Cast("'0001-01-01 00:00:00 -14:00'"), Cast($"'9999-12-31 23:59:59{fraction} +14:00'")],
            _ => throw new InvalidOperationException($"No edges for {type.Text}."),
        };
    }

    [Fact]
    public async Task A_date_or_time_rounded_to_fewer_fractional_digits_never_overflows_since_the_greatest_is_kept()
    {
        // Elsewhere a value rounds to the nearest (half a second up into the next day, and year); at the greatest value of the
        // type the server keeps it rather than overflow, whatever the offset.
        var outcome = await ObservedCall.Run(server, [], """
            SET ANSI_WARNINGS OFF; SET ARITHABORT OFF;
            SELECT CONVERT(varchar(40), CAST(CAST('9999-12-31 23:59:59.9999999' AS datetime2(7)) AS datetime2(0)), 121) AS a,
                   CONVERT(varchar(40), CAST(CAST('9999-12-31 23:59:59.997' AS datetime) AS datetime2(0)), 121) AS b,
                   CONVERT(varchar(40), CAST(CAST('23:59:59.9999999' AS time(7)) AS time(0)), 121) AS c,
                   CONVERT(varchar(40), CAST(CAST('9999-12-31 23:59:59.9999999 +14:00' AS datetimeoffset(7)) AS datetimeoffset(0)), 121) AS d,
                   CONVERT(varchar(40), CAST(CAST('2020-12-31 23:59:59.5' AS datetime2(1)) AS datetime2(0)), 121) AS e;
            """);
        Assert.Null(outcome.Error);
        Assert.Equal(
            ["a,b,c,d,e: 9999-12-31 23:59:59,9999-12-31 23:59:59,23:59:59,9999-12-31 23:59:59 +14:00,2021-01-01 00:00:00"],
            outcome.Sets);
    }

    [Fact]
    public async Task Each_conversion_the_table_lists_keeps_every_value_at_the_edges_of_its_types()
    {
        var instances = BuiltInType.All.Where(SqlTypes.IsModelled).SelectMany(Instances).ToList();
        var failures = new List<string>();
        var checkedRows = new HashSet<ConversionRow>();
        foreach (var to in instances)
        {
            var claims = instances
                .Where(from => from.Text != to.Text && Conversions.Table.Any(r => r.To.Contains(to.Type) && r.From.Contains(from.Type)) && Conversions.Converts(Operand.OfType(from), to))
                .SelectMany(from => Edges(from).Select(value => (From: from, Value: value)))
                .ToList();
            if (claims.Count == 0)
                continue;
            foreach (var (from, _) in claims)
                checkedRows.Add(Conversions.Table.First(r => r.To.Contains(to.Type) && r.From.Contains(from.Type)));

            // Whether each value came back, since the client can't hold every value (a decimal of 38 digits).
            var columns = string.Join(", ", claims.Select((c, i) => $"CASE WHEN CAST({c.Value} AS {to.Text}) IS NULL THEN 'null' ELSE 'kept' END AS c{i.ToString(CultureInfo.InvariantCulture)}"));
            var outcome = await ObservedCall.Run(server, [], $"SET ANSI_WARNINGS OFF; SET ARITHABORT OFF; SELECT {columns};");
            if (outcome.Error is { } error)
            {
                failures.Add($"into {to.Text}: error {error}");
                continue;
            }

            var values = outcome.Sets[0][(outcome.Sets[0].IndexOf(": ", StringComparison.Ordinal) + 2)..].Split(',');
            failures.AddRange(claims.Where((c, i) => values[i] == "null").Select(c => $"{c.From.Text} into {to.Text}: {c.Value} overflows"));
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
        Assert.True(checkedRows.Count == Conversions.Table.Count, $"{Conversions.Table.Count - checkedRows.Count} rows reached no pair of the grid.");
    }
}
