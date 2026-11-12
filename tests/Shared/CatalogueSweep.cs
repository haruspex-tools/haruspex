using System.Globalization;
using System.Text;
using Haruspex.Cli;
using Haruspex.Ir;

namespace Haruspex.TestSupport;

/// <summary>
/// Every built-in type through the operators that type a value: arithmetic, a sign, <c>CAST</c>, <c>ISNULL</c> and the
/// aggregates, over a column of each type and a literal of each kind. Fixtures reach only the entries their procedures use,
/// and a rule broken for another entry reached no fixture (<c>COUNT</c>'s digits, found only in a private corpus); this
/// reaches them all. Two tests share it: <c>CatalogueSweepTests</c> holds what haruspex infers for each expression as a
/// snapshot, so a change to any entry shows, and <c>CatalogueSweepGridTests</c> checks each typed expression against the type
/// SQL Server describes for it, so the snapshot holds only what the server does.
/// </summary>
internal static class CatalogueSweep
{
    /// <summary>A column of each kind of type, with the arguments that reach the rules' edges, and the types nothing models.</summary>
    public static readonly (string Name, string Type)[] Columns =
    [
        ("bt", "bit"), ("ti", "tinyint"), ("si", "smallint"), ("i", "int"), ("bi", "bigint"),
        ("d52", "decimal(5,2)"), ("d380", "decimal(38,0)"), ("d3838", "decimal(38,38)"), ("n102", "numeric(10,2)"),
        ("m", "money"), ("sm", "smallmoney"), ("f", "float"), ("r", "real"),
        ("c3", "char(3)"), ("v10", "varchar(10)"), ("vx", "varchar(max)"), ("nc3", "nchar(3)"), ("nv10", "nvarchar(10)"), ("nvx", "nvarchar(max)"),
        ("dt", "date"), ("tm3", "time(3)"), ("dtm", "datetime"), ("dt23", "datetime2(3)"), ("sdt", "smalldatetime"), ("dto3", "datetimeoffset(3)"),
        ("u", "uniqueidentifier"),
        ("bin4", "binary(4)"), ("vb10", "varbinary(10)"), ("x", "xml"), ("sv", "sql_variant"), ("j", "json"),
    ];

    /// <summary>A literal of each kind: an integer (which counts its own digits), a decimal, strings, a float and money.</summary>
    public static readonly string[] Literals = ["1", "12345", "2.50", "'ab'", "N'ab'", "1e0", "$1"];

    /// <summary>The types a value is converted to by <c>CAST</c>: each column's, and a string without a length.</summary>
    public static readonly string[] Targets = [.. Columns.Select(c => c.Type), "varchar", "nvarchar"];

    /// <summary>The operators the sweep runs, each a snapshot and a theory case of its own.</summary>
    public static readonly string[] Forms = ["add", "subtract", "multiply", "divide", "modulo", "negate", "cast", "isnull", "aggregate"];

    /// <summary>Columns and literals, as operands.</summary>
    public static IEnumerable<string> Operands => [.. Columns.Select(c => c.Name), .. Literals];

    /// <summary>The table every expression reads, with a column of each type, never null.</summary>
    public static string Table =>
        $"CREATE TABLE dbo.g (id int NOT NULL PRIMARY KEY, {string.Join(", ", Columns.Select(c => $"{c.Name} {c.Type} NOT NULL"))});";

    /// <summary>The expressions of one form, in a stable order.</summary>
    public static IReadOnlyList<string> Expressions(string form) => form switch
    {
        "add" => Pairs("+"),
        "subtract" => Pairs("-"),
        "multiply" => Pairs("*"),
        "divide" => Pairs("/"),
        "modulo" => Pairs("%"),
        "negate" => [.. Operands.Select(o => $"-{o}")],
        "cast" => [.. Operands.SelectMany(o => Targets.Select(t => $"CAST({o} AS {t})"))],
        "isnull" => [.. Operands.SelectMany(a => Operands.Select(b => $"ISNULL({a}, {b})"))],
        "aggregate" => Aggregates(),
        _ => throw new ArgumentOutOfRangeException(nameof(form), form, "Not a form of the sweep."),
    };

    private static List<string> Pairs(string op) => [.. Operands.SelectMany(a => Operands.Select(b => $"{a} {op} {b}"))];

    /// <summary>
    /// Each aggregate over each column, and the counts beside every operand under every operator: an aggregate's result is a
    /// value like any other, and the digits it counts beside a decimal are its own rule. A column beside an aggregate is read
    /// through <c>MIN</c>, which keeps its type, since a query without GROUP BY can't read a column bare.
    /// </summary>
    private static List<string> Aggregates()
    {
        string[] functions = ["COUNT", "COUNT_BIG", "SUM", "AVG", "MIN", "MAX", "STDEV", "VAR"];
        var all = Columns.SelectMany(c => functions.Select(f => $"{f}({c.Name})")).ToList();
        var beside = Columns.Select(c => $"MIN({c.Name})").Concat(Literals).ToList();
        foreach (var count in new[] { "COUNT(*)", "COUNT_BIG(*)" })
        {
            foreach (var op in new[] { "+", "-", "*", "/", "%" })
                all.AddRange(beside.SelectMany(o => new[] { $"{count} {op} {o}", $"{o} {op} {count}" }));
        }

        return all;
    }

    /// <summary>What haruspex infers for one expression: its SQL type, or null with the unknown's reason and detail.</summary>
    /// <param name="Expression">The expression, as written in the query.</param>
    /// <param name="Field">The field, when the result is typed.</param>
    /// <param name="Unknown">The unknown, when it is not.</param>
    public sealed record Outcome(string Expression, IrField? Field, IrUnknown? Unknown)
    {
        /// <summary>The SQL type haruspex gives it, or null when it is unknown.</summary>
        public string? Sql => Field?.Type is IrScalar { Sql: var sql } ? sql : null;

        /// <summary>
        /// One line: the expression, then its type, the reasons it may be null and the facets of its type; or <c>unknown</c>, the
        /// reason and the detail.
        /// </summary>
        public override string ToString()
        {
            if (Field?.Type is not IrScalar scalar)
                return $"{Expression} -> unknown {(Unknown is null ? "(no unknown recorded)" : $"{IrWireNames.Of(Unknown.Reason)}: {Unknown.Detail}")}";
            var line = new StringBuilder($"{Expression} -> {scalar.Sql ?? "(no sql)"} {IrWireNames.Of(scalar.Kind)}");
            if (scalar.Format is { } format)
                line.Append(CultureInfo.InvariantCulture, $"/{IrWireNames.Of(format)}");
            if (Field.NullableBecause.Count > 0)
                line.Append(" null:").AppendJoin(',', Field.NullableBecause.Select(r => IrWireNames.Of(r.Rule)));
            if (scalar.Facets is { } facets)
                line.Append(' ').Append(Facets(facets));
            return line.ToString();
        }

        private static string Facets(IrFacets f)
        {
            var parts = new List<string>();
            if (f.Length is { } l)
                parts.Add($"length{(l.IsFixed ? "=" : "<=")}{l.Value.ToString(CultureInfo.InvariantCulture)}");
            if (f.Digits is { } d)
                parts.Add($"digits={d.Precision.ToString(CultureInfo.InvariantCulture)},{d.Scale.ToString(CultureInfo.InvariantCulture)}");
            if (f.Min is { } min)
                parts.Add($"{(min.Inclusive ? "[" : "(")}{min.Value}");
            if (f.Max is { } max)
                parts.Add($"{max.Value}{(max.Inclusive ? "]" : ")")}");
            return "{" + string.Join(" ", parts) + "}";
        }
    }

    /// <summary>
    /// Infers each expression of a form as a procedure of its own over <see cref="Table"/>, so that one unknown can't hide
    /// another, and gives the outcomes in the order of <see cref="Expressions"/>.
    /// </summary>
    public static IReadOnlyList<Outcome> Infer(string form)
    {
        var expressions = Expressions(form);
        using var dir = new TempDirectory();
        dir.File("tables.sql", Table);
        for (var n = 0; n < expressions.Count; n++)
            dir.File($"p{n.ToString(CultureInfo.InvariantCulture)}.sql", $"CREATE PROCEDURE dbo.p{n.ToString(CultureInfo.InvariantCulture)} AS\n{Query(expressions[n])};\n");

        var project = SqlProject.Load(dir.Path);
        if (project.Diagnostics.Count > 0)
            throw new InvalidOperationException(string.Join("\n", project.Diagnostics));
        var document = project.Infer(project.Procedures);
        // Procedures are named p0, p1, … after their expression's position, and the IR's name ends with the number.
        var byNumber = document.Objects.ToDictionary(o => int.Parse(string.Concat(o.Name.Reverse().TakeWhile(char.IsAsciiDigit).Reverse()), CultureInfo.InvariantCulture));
        if (byNumber.Count != expressions.Count)
            throw new InvalidOperationException($"{byNumber.Count} procedures inferred for {expressions.Count} expressions.");
        return [.. expressions.Select((e, n) => OutcomeOf(document, byNumber[n], e))];
    }

    /// <summary>The query that reads an expression: over the table, so that the server can't fold it to a constant.</summary>
    public static string Query(string expression) => $"SELECT {expression} AS e FROM dbo.g";

    private static Outcome OutcomeOf(IrDocument document, IrObject procedure, string expression)
    {
        if (procedure.Results is [{ Type: IrArray { Items: IrRef row } }] && document.Types[row.Name].Fields is [{ Type: IrScalar } field])
            return new Outcome(expression, field, null);
        return new Outcome(expression, null, document.Unknowns.FirstOrDefault(u => u.Object == procedure.Id));
    }
}
