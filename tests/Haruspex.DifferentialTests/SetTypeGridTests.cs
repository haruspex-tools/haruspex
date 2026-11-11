using System.Globalization;
using Haruspex.Cli;
using Haruspex.DifferentialTests.Server;
using Haruspex.Ir;

namespace Haruspex.DifferentialTests;

/// <summary>
/// The type haruspex gives a column whose values have different types (a set operation's, <c>COALESCE</c>'s, a <c>CASE</c>'s)
/// is the one SQL Server describes, over every ordered pair of a grid of types that reaches the rules' edges: decimals past 38
/// digits, integers, bit, money, floats, strings of each kind up to max, every date and time type, uniqueidentifier, and
/// literals. A pair SQL Server refuses (206, an operand type clash) is unknown in haruspex, and its reason names both types.
/// The rules are in Haruspex.SqlServer.Inference's README.md, "Combining types", observed in <c>SetTypeObservationTests</c>; this
/// checks them through the whole pipeline: procedures are inferred from SQL and each column compared with the server's
/// description of the same query.
/// </summary>
public class SetTypeGridTests(SqlServerFixture server)
{
    /// <summary>The operand columns: name and type.</summary>
    private static readonly (string Name, string Type)[] Columns =
    [
        ("bt", "bit"), ("ti", "tinyint"), ("si", "smallint"), ("i", "int"), ("bi", "bigint"),
        ("d52", "decimal(5,2)"), ("n92", "numeric(9,2)"), ("d2010", "decimal(20,10)"), ("d300", "decimal(30,0)"), ("d380", "decimal(38,0)"),
        ("d3810", "decimal(38,10)"), ("d3838", "decimal(38,38)"), ("m", "money"), ("sm", "smallmoney"), ("f", "float"), ("r", "real"),
        ("c10", "char(10)"), ("v10", "varchar(10)"), ("v8000", "varchar(8000)"), ("vmax", "varchar(max)"), ("nc5", "nchar(5)"),
        ("nv20", "nvarchar(20)"), ("nvmax", "nvarchar(max)"),
        ("dt", "date"), ("tm3", "time(3)"), ("tm7", "time(7)"), ("dtm", "datetime"), ("sdt", "smalldatetime"), ("dt23", "datetime2(3)"),
        ("dt27", "datetime2(7)"), ("dto0", "datetimeoffset(0)"), ("dto3", "datetimeoffset(3)"), ("u", "uniqueidentifier"),
    ];

    /// <summary>Literals beside a column: an integer literal counts its own digits, a decimal literal is numeric of them.</summary>
    private static readonly string[] Literals = ["1", "12345", "2.5", "'abc'", "N'abcdef'"];

    /// <summary>Every ordered pair of different operands, columns and literals: (left, right).</summary>
    private static IEnumerable<(string Left, string Right)> Pairs()
    {
        foreach (var (a, _) in Columns)
        {
            foreach (var (b, _) in Columns)
            {
                if (a != b)
                    yield return (a, b);
            }

            foreach (var literal in Literals)
            {
                yield return (a, literal);
                yield return (literal, a);
            }
        }
    }

    public static TheoryData<string> Forms { get; } = ["union", "coalesce", "case"];

    [Theory]
    [MemberData(nameof(Forms))]
    public async Task Each_pair_has_the_type_sql_server_describes_or_is_unknown_naming_both_where_it_is_refused(string form)
    {
        server.Require();
        // COALESCE whose first argument is a string literal is folded by the server to that literal beside a string of its kind
        // or a lesser one, and typed by it alone (observed: Coalesce_of_a_string_literal_first_is_folded_to_it_beside_a_string);
        // haruspex gives the type over every argument, which holds every value, so those pairs are left out here.
        var pairs = Pairs().Where(p => form != "coalesce" || !(p.Left.Contains('\'', StringComparison.Ordinal) && IsString(p.Right))).ToList();
        string Name(int i) => $"e{i.ToString(CultureInfo.InvariantCulture)}";

        // The server first: each pair described on its own, since a refused pair fails its whole query, over a table (a row of
        // constants would let the server fold COALESCE and CASE to one argument and type them by it), in one batch.
        var table = $"CREATE TABLE dbo.g (id int NOT NULL PRIMARY KEY, {string.Join(", ", Columns.Select(c => $"{c.Name} {c.Type} NOT NULL"))});";
        var batch = new System.Text.StringBuilder("SET NOCOUNT ON; DECLARE @r TABLE (n int NOT NULL, t nvarchar(200) NULL, e int NULL);\n");
        for (var i = 0; i < pairs.Count; i++)
        {
            var query = Query(form, pairs[i], Name(i), "dbo.g").Replace("'", "''", StringComparison.Ordinal);
            batch.Append(CultureInfo.InvariantCulture, $"INSERT INTO @r SELECT TOP (1) {i}, system_type_name, error_number FROM sys.dm_exec_describe_first_result_set(N'{query}', NULL, 0) ORDER BY column_ordinal;\n");
        }

        batch.Append("SELECT CONCAT(n, ' ', ISNULL(t, CONCAT('!', e))) AS c FROM @r ORDER BY n;");
        var outcome = await ObservedCall.Run(server, [table], batch.ToString());
        Assert.Null(outcome.Error);
        var described = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var line in outcome.Sets[0]["c: ".Length..].Split(';'))
        {
            var (n, type) = (line[..line.IndexOf(' ', StringComparison.Ordinal)], line[(line.IndexOf(' ', StringComparison.Ordinal) + 1)..]);
            Assert.True(type is not ['!', ..] || type == "!206", $"{Query(form, pairs[int.Parse(n, CultureInfo.InvariantCulture)], "e" + n, "g")}: error {type}");
            described["e" + n] = type.StartsWith('!') ? null : type;
        }

        Assert.Equal(pairs.Count, described.Count);

        var (inferred, unknowns) = Infer(form, pairs);
        var differences = new List<string>();
        for (var i = 0; i < pairs.Count; i++)
        {
            var (left, right) = pairs[i];
            var sql = described[Name(i)];
            if (sql is not null)
            {
                if (!string.Equals(inferred.GetValueOrDefault(Name(i)), sql, StringComparison.OrdinalIgnoreCase))
                    differences.Add($"{left}, {right}: haruspex {inferred.GetValueOrDefault(Name(i)) ?? "(unknown: " + unknowns.GetValueOrDefault(Name(i)) + ")"}, SQL Server {sql}");
                continue;
            }

            // Refused: unknown, naming both types as SQL Server's message does.
            var why = unknowns.GetValueOrDefault(Name(i));
            var (lt, rt) = (TypeOf(left), TypeOf(right));
            if (inferred.ContainsKey(Name(i)) || why is null || !why.Contains(lt, StringComparison.Ordinal) || !why.Contains(rt, StringComparison.Ordinal))
                differences.Add($"{left}, {right}: SQL Server refuses it; haruspex {inferred.GetValueOrDefault(Name(i)) ?? "unknown: " + why}");
        }

        Assert.True(differences.Count == 0, $"{differences.Count} of {pairs.Count} differ:\n" + string.Join("\n", differences));
    }

    private static bool IsString(string operand) => TypeOf(operand) is var t && (t.Contains("char", StringComparison.Ordinal));

    /// <summary>An operand's type as haruspex names it: a column's own, or a literal's.</summary>
    private static string TypeOf(string operand) => Columns.FirstOrDefault(c => c.Name == operand).Type ?? operand switch
    {
        ['\'', ..] => "varchar",
        ['N', '\'', ..] => "nvarchar",
        _ when operand.Contains('.', StringComparison.Ordinal) => "numeric",
        _ => "int",
    };

    /// <summary>One pair in its form, as a query over <paramref name="source"/>, its column named <paramref name="name"/>.</summary>
    private static string Query(string form, (string Left, string Right) pair, string name, string source) => form switch
    {
        "union" => $"SELECT {pair.Left} AS {name} FROM {source} UNION ALL SELECT {pair.Right} FROM {source}",
        "coalesce" => $"SELECT COALESCE({pair.Left}, {pair.Right}) AS {name} FROM {source}",

        // A condition the server cannot fold, so both results count (6g: a CASE folded to one branch is typed by it).
        _ => $"SELECT CASE WHEN id > 0 THEN {pair.Left} ELSE {pair.Right} END AS {name} FROM {source}",
    };

    /// <summary>
    /// The SQL type haruspex infers for each pair, by name, and for those it leaves unknown, the reason's detail. Each pair is a
    /// procedure of its own, as on the server, so one unknown cannot hide another.
    /// </summary>
    private static (Dictionary<string, string> Types, Dictionary<string, string> Unknowns) Infer(string form, List<(string Left, string Right)> pairs)
    {
        var dir = Directory.CreateTempSubdirectory("haruspex-set-grid-");
        try
        {
            var columns = string.Join(", ", Columns.Select(c => $"{c.Name} {c.Type} NOT NULL"));
            File.WriteAllText(Path.Join(dir.FullName, "tables.sql"), $"CREATE TABLE dbo.g (id int NOT NULL PRIMARY KEY, {columns});\n");
            for (var i = 0; i < pairs.Count; i++)
            {
                var n = i.ToString(CultureInfo.InvariantCulture);
                File.WriteAllText(Path.Join(dir.FullName, $"p{n}.sql"), $"CREATE PROCEDURE dbo.p{n} AS\n{Query(form, pairs[i], $"e{n}", "dbo.g")};\n");
            }

            var project = SqlProject.Load(dir.FullName);
            Assert.Empty(project.Diagnostics);
            var document = project.Infer(project.Procedures);
            var types = new Dictionary<string, string>(StringComparer.Ordinal);
            var unknowns = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var procedure in document.Objects)
            {
                var name = "e" + procedure.Name[(procedure.Name.LastIndexOf(".p", StringComparison.Ordinal) + 2)..];
                if (procedure.Results is [{ Type: IrArray { Items: IrRef row } }]
                    && document.Types[row.Name].Fields is [{ Type: IrScalar { Sql: { } sql } }])
                {
                    types[name] = sql;
                    continue;
                }

                unknowns[name] = string.Join("; ", document.Unknowns.Where(u => u.Object == procedure.Id || u.Object == procedure.Name).Select(u => u.Detail));
            }

            return (types, unknowns);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
