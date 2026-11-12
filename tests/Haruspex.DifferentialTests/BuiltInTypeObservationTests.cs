using System.Globalization;
using System.Text;
using Haruspex.DifferentialTests.Server;
using Haruspex.SqlServer.Semantics;

namespace Haruspex.DifferentialTests;

/// <summary>
/// Every row of the catalogue of built-in types (<see cref="BuiltInType"/>) as SQL Server has it, attribute by attribute: a
/// wrong digit, range or rank in any row, used by a fixture or not, fails one of these. Each test reads every type the
/// attribute applies to from the server and compares it with the row.
/// </summary>
public class BuiltInTypeObservationTests(SqlServerFixture server)
{
    /// <summary>A column of each type, declared by its name alone, so the server applies its defaults.</summary>
    private static readonly BuiltInType[] Declarable = [.. BuiltInType.All.Where(t => t != BuiltInType.Vector)];

    private static string Column(BuiltInType type) => "c_" + type.Name;

    /// <summary>The table every test reads: a column of each type, and a vector of 3 dimensions, which takes no default.</summary>
    private static readonly string Table =
        $"CREATE TABLE dbo.t (id int NOT NULL PRIMARY KEY, {string.Join(", ", Declarable.Select(t => $"{Column(t)} {t.Name} NULL"))}, c_vector vector(3) NULL);";

    [Fact]
    public async Task Each_types_name_digits_and_defaults_are_what_the_server_describes()
    {
        var described = (await Rows(
            "SELECT CONCAT_WS('|', name, system_type_name, precision, scale, max_length) AS c FROM sys.dm_exec_describe_first_result_set(N'SELECT * FROM dbo.t', NULL, 0) ORDER BY column_ordinal",
            [Table]))
            .ToDictionary(r => r[0], StringComparer.Ordinal);
        var differences = new List<string>();
        foreach (var type in Declarable)
        {
            var row = described[Column(type)];
            var (name, precision, scale, maxLength) = (row[1], int.Parse(row[2], CultureInfo.InvariantCulture), int.Parse(row[3], CultureInfo.InvariantCulture), int.Parse(row[4], CultureInfo.InvariantCulture));
            void Expect(bool holds, string what)
            {
                if (!holds)
                    differences.Add($"{type.Name}: {what}; the server describes {name}, precision {precision}, scale {scale}, max_length {maxLength}");
            }

            Expect(name.Split('(')[0] == type.Name, "its name");
            if (type.Digits is var (p, s))
                Expect(precision == p && scale == s, $"digits ({p}, {s})");
            if (type.FractionalSeconds is { } fraction)
                Expect(scale == fraction, $"{fraction} fractional digits");
            switch (type.Arguments)
            {
                case TypeArgumentKind.Length:
                    Expect(name == $"{type.Name}({BuiltInType.LengthDeclared})", $"declared length {BuiltInType.LengthDeclared}");
                    Expect(maxLength == (type.IsNational ? 2 : 1) * BuiltInType.LengthDeclared, type.IsNational ? "two bytes a character" : "one byte a character");
                    break;
                case TypeArgumentKind.PrecisionAndScale:
                    Expect((precision, scale) == BuiltInType.DigitsDeclared, $"declared digits {BuiltInType.DigitsDeclared}");
                    break;
                case TypeArgumentKind.FractionalSeconds:
                    Expect(scale == BuiltInType.FractionalSecondsDeclared, $"declared fractional digits {BuiltInType.FractionalSecondsDeclared}");
                    break;
            }
        }

        Assert.True(differences.Count == 0, string.Join("\n", differences));
    }

    [Fact]
    public async Task Rowversion_is_another_name_for_timestamp()
    {
        var row = Assert.Single(await Rows(
            "SELECT system_type_name AS c FROM sys.dm_exec_describe_first_result_set(N'SELECT v FROM dbo.r', NULL, 0)",
            ["CREATE TABLE dbo.r (id int NOT NULL PRIMARY KEY, v rowversion NOT NULL);"]));
        Assert.Equal(BuiltInType.Named("rowversion")!.Name, row[0]);
    }

    [Fact]
    public async Task A_string_or_binary_type_converted_without_a_length_is_30_long()
    {
        var types = BuiltInType.All.Where(t => t.Arguments == TypeArgumentKind.Length).ToList();
        var described = await Describe([.. types.Select(t => $"SELECT CAST(c_int AS {t.Name}) AS e FROM dbo.t")]);
        Assert.Equal(types.Select(t => $"{t.Name}({BuiltInType.LengthInConversion})"), described);
    }

    [Fact]
    public async Task A_length_types_longest_length_and_whether_it_takes_max_are_as_listed()
    {
        var differences = new List<string>();
        foreach (var type in BuiltInType.All.Where(t => t.Arguments == TypeArgumentKind.Length))
        {
            async Task<bool> Declares(string arguments) =>
                (await ObservedCall.Run(server, [], $"DECLARE @v {type.Name}({arguments}); SELECT 1 AS c;")).Error is null;

            if (!await Declares(type.MostLength.ToString(CultureInfo.InvariantCulture)) || await Declares((type.MostLength + 1).ToString(CultureInfo.InvariantCulture)))
                differences.Add($"{type.Name}: longest {type.MostLength}");
            if (await Declares("max") != type.IsVarying)
                differences.Add($"{type.Name}: {(type.IsVarying ? "takes" : "doesn't take")} max");
        }

        Assert.True(differences.Count == 0, string.Join("\n", differences));
    }

    [Fact]
    public async Task An_exact_types_range_holds_its_least_and_greatest_and_nothing_past_them()
    {
        var differences = new List<string>();
        foreach (var type in BuiltInType.All.Where(t => t.Least is not null))
        {
            var (least, greatest) = (type.Least!.Value, type.Greatest!.Value);
            var step = ExactDecimal.Of(1, least.Scale);
            var below = ExactDecimal.Parse((decimal.Parse(least.ToString(), CultureInfo.InvariantCulture) - decimal.Parse(step.ToString(), CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture));
            var above = ExactDecimal.Parse((decimal.Parse(greatest.ToString(), CultureInfo.InvariantCulture) + decimal.Parse(step.ToString(), CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture));

            // Past either end the conversion fails, or (for bit, where any number but 0 is 1) lands inside the range.
            var row = Assert.Single(await Rows(
                $"SELECT CONCAT_WS('|', {Text($"CAST({least} AS {type.Name})")}, {Text($"CAST({greatest} AS {type.Name})")}, ISNULL({Text($"TRY_CAST({below} AS {type.Name})")}, 'none'), ISNULL({Text($"TRY_CAST({above} AS {type.Name})")}, 'none')) AS c",
                []));
            // Style 2 writes money with its four places, where text by default has two; other types ignore it.
            static string Text(string value) => $"CONVERT(varchar(60), {value}, 2)";
            bool Inside(string value) => value == "none" || (ExactDecimal.Parse(value) >= least && ExactDecimal.Parse(value) <= greatest);
            if (ExactDecimal.Parse(row[0]).CompareTo(least) != 0 || ExactDecimal.Parse(row[1]).CompareTo(greatest) != 0 || !Inside(row[2]) || !Inside(row[3]))
                differences.Add($"{type.Name}: [{least}, {greatest}]; the server gives {string.Join(", ", row)}");
        }

        Assert.True(differences.Count == 0, string.Join("\n", differences));
    }

    [Fact]
    public async Task A_types_longest_text_is_as_long_as_its_longest_value_written_as_text()
    {
        var differences = new List<string>();
        foreach (var type in BuiltInType.All.Where(t => t.LongestText is not null))
        {
            var values = type.Least is { } least ? $"(CAST({least} AS {type.Name})), (CAST({type.Greatest} AS {type.Name}))" : $"(CAST(NEWID() AS {type.Name}))";
            var row = Assert.Single(await Rows($"SELECT MAX(LEN(CAST(v AS varchar(60)))) AS c FROM (VALUES {values}) AS x (v)", []));
            if (int.Parse(row[0], CultureInfo.InvariantCulture) != type.LongestText)
                differences.Add($"{type.Name}: {type.LongestText} characters; the server writes {row[0]}");
        }

        Assert.True(differences.Count == 0, string.Join("\n", differences));
    }

    [Fact]
    public async Task Of_two_types_that_convert_coalesce_gives_the_one_of_higher_precedence()
    {
        var ranked = Declarable.Where(t => t.Precedence is not null).ToList();
        var pairs = ranked.SelectMany(a => ranked.Where(b => b != a).Select(b => (A: a, B: b))).ToList();
        var described = await Describe([.. pairs.Select(p => $"SELECT COALESCE({Column(p.A)}, {Column(p.B)}) AS e FROM dbo.t")]);

        var differences = new List<string>();
        var observed = new HashSet<BuiltInType>();
        for (var i = 0; i < pairs.Count; i++)
        {
            var (a, b) = pairs[i];
            if (described[i].StartsWith('!'))
                continue;
            var result = BuiltInType.Named(described[i].Split('(')[0]);
            var expected = Expected(a, b);
            if (!expected.Contains(result))
                differences.Add($"{a.Name} with {b.Name}: the catalogue ranks {string.Join(" or ", expected.Select(t => t.Name))} higher; the server gives {described[i]}");
            observed.Add(a);
            observed.Add(b);
        }

        Assert.True(differences.Count == 0, string.Join("\n", differences));
        Assert.True(observed.SetEquals(ranked), "Never met a type it converts with: " + string.Join(", ", ranked.Except(observed).Select(t => t.Name)));
    }

    /// <summary>
    /// The types the catalogue says <c>COALESCE</c> of these two gives: the one of higher precedence (either, when they rank
    /// the same), except that of two types of a length, strings or binary, it is of the higher one's family, national if either
    /// is and varying if either is (nchar with varchar is nvarchar), as the server's type rule for a chain says
    /// (<c>TypeChain.Of</c>; observed in <c>SetTypeGridTests</c>).
    /// </summary>
    private static BuiltInType[] Expected(BuiltInType a, BuiltInType b)
    {
        var higher = a.Precedence <= b.Precedence ? a : b;
        if (a.Arguments != TypeArgumentKind.Length || b.Arguments != TypeArgumentKind.Length)
            return a.Precedence == b.Precedence ? [a, b] : [higher];
        return [.. BuiltInType.All.Where(t => t.Arguments == TypeArgumentKind.Length && t.Family == higher.Family
            && t.IsNational == (a.IsNational || b.IsNational) && t.IsVarying == (a.IsVarying || b.IsVarying))];
    }

    /// <summary>The rows of a query's one column <c>c</c>, each split at <c>|</c>.</summary>
    private async Task<List<string[]>> Rows(string query, string[] setup)
    {
        var outcome = await ObservedCall.Run(server, setup, query);
        Assert.Null(outcome.Error);
        var text = outcome.Sets[0]["c: ".Length..];
        return text.Length == 0 ? [] : [.. text.Split(';').Select(r => r.Split('|'))];
    }

    /// <summary>
    /// The type the server describes for each query's one column over the table, or <c>!</c> and the error it gives: each
    /// described on its own, since an error fails its whole query, in one batch.
    /// </summary>
    private async Task<string[]> Describe(List<string> queries)
    {
        var batch = new StringBuilder("SET NOCOUNT ON; DECLARE @r TABLE (n int NOT NULL, t nvarchar(200) NULL, e int NULL);\n");
        for (var i = 0; i < queries.Count; i++)
        {
            var query = queries[i].Replace("'", "''", StringComparison.Ordinal);
            batch.Append(CultureInfo.InvariantCulture, $"INSERT INTO @r SELECT TOP (1) {i}, system_type_name, error_number FROM sys.dm_exec_describe_first_result_set(N'{query}', NULL, 0) ORDER BY column_ordinal;\n");
        }

        batch.Append("SELECT CONCAT(n, '|', ISNULL(t, CONCAT('!', e))) AS c FROM @r ORDER BY n;");
        var types = new string[queries.Count];
        foreach (var row in await Rows(batch.ToString(), [Table]))
            types[int.Parse(row[0], CultureInfo.InvariantCulture)] = row[1];
        return types;
    }
}
