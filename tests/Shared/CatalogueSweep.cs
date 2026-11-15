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
        ("m", "money"), ("sm", "smallmoney"), ("f", "float"), ("r", "real"), ("f24", "float(24)"), ("f25", "float(25)"),
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
    public static readonly string[] Forms = ["add", "subtract", "multiply", "divide", "modulo", "negate", "cast", "isnull", "aggregate", "function", "window"];

    /// <summary>
    /// A call of each built-in function inference reads, with <c>{0}</c> where each operand goes in turn; a call without it is
    /// read once. Each function's other arguments are ordinary ones, so the operand's type is what varies.
    /// </summary>
    public static readonly string[] Functions =
    [
        "DATEDIFF(day, {0}, dt)", "DATEDIFF_BIG(day, {0}, dt)", "DATEADD(day, 1, {0})", "DATEADD(day, {0}, dt)", "DATEPART(year, {0})",
        "YEAR({0})", "MONTH({0})", "DAY({0})", "DATENAME(month, {0})", "EOMONTH({0})", "EOMONTH(dt, {0})",
        "DATEFROMPARTS({0}, 1, 1)", "DATETIMEFROMPARTS({0}, 1, 1, 0, 0, 0, 0)", "SMALLDATETIMEFROMPARTS({0}, 1, 1, 0, 0)",
        "DATETIME2FROMPARTS({0}, 1, 1, 0, 0, 0, 0, 3)", "TIMEFROMPARTS({0}, 0, 0, 0, 3)", "DATETIMEOFFSETFROMPARTS({0}, 1, 1, 0, 0, 0, 0, 0, 0, 3)",
        "DATETRUNC(month, {0})", "DATE_BUCKET(week, 2, {0})", "DATE_BUCKET(day, {0}, dt)", "SWITCHOFFSET({0}, '+01:00')",
        "SWITCHOFFSET(dto3, {0})", "TODATETIMEOFFSET({0}, '-05:00')", "ISDATE({0})",
        "GETDATE()", "GETUTCDATE()", "SYSDATETIME()", "SYSUTCDATETIME()", "SYSDATETIMEOFFSET()", "CURRENT_TIMESTAMP",
        "LTRIM({0})", "RTRIM({0})", "TRIM({0})", "LTRIM({0}, 'x')", "RTRIM({0}, N'x')", "TRIM('x' FROM {0})", "UPPER({0})", "LOWER({0})",
        "SUBSTRING({0}, 1, 2)", "LEFT({0}, 2)", "RIGHT({0}, 2)", "LEN({0})", "DATALENGTH({0})", "REPLACE({0}, 'a', 'b')",
        "STRING_ESCAPE({0}, 'json')", "FORMATMESSAGE({0}, 1)", "JSON_VALUE({0}, '$.a')", "JSON_QUERY({0})",
        "ROUND({0}, 1)", "ROUND({0}, 0, 1)", "FLOOR({0})", "CEILING({0})", "ABS({0})", "SIGN({0})", "POWER({0}, 2)", "SQUARE({0})",
        "SQRT({0})", "EXP({0})", "LOG({0})", "LOG({0}, 2)", "LOG10({0})",
        "SIN({0})", "COS({0})", "TAN({0})", "COT({0})", "ASIN({0})", "ACOS({0})", "ATAN({0})", "ATN2({0}, 1)", "DEGREES({0})",
        "RADIANS({0})", "PI()", "GREATEST({0}, {0})", "LEAST({0}, 1)", "GREATEST({0}, v10)", "ISJSON({0})", "NEWID()",
        "ASCII({0})", "UNICODE({0})", "CHAR({0})", "CHARINDEX('a', {0})", "CHARINDEX({0}, 'abc', 2)", "PATINDEX('%a%', {0})",
        "REPLICATE({0}, 3)", "REPLICATE('ab', {0})", "SPACE({0})", "REVERSE({0})", "STUFF({0}, 1, 1, 'x')", "STUFF('abc', 2, 1, {0})",
        "STUFF({0}, 2, 5, N'xyz')", "STUFF({0}, id, 1, 'x')", "QUOTENAME({0})", "QUOTENAME({0}, '`')", "CHARINDEX('a', 'abc', {0})", "SUBSTRING('abc', {0}, 1)", "SUBSTRING('abc', 1, {0})",
        "LEFT('abc', {0})", "RIGHT('abc', {0})", "STR({0})", "STR({0}, 5, 2)", "TRANSLATE({0}, 'a', 'b')",
        "SOUNDEX({0})", "DIFFERENCE({0}, 'a')",
        "CONCAT({0}, 'x')", "CONCAT_WS(',', {0}, 'x')", "CHOOSE(id, {0}, {0})", "IIF(id > 1, {0}, {0})", "NULLIF({0}, {0})",
        "COALESCE({0}, NULL)", "TRY_CAST({0} AS int)", "CONVERT(varchar(30), {0}, 1)", "TRY_CONVERT(int, {0})",
        "JSON_OBJECT('k': {0})", "JSON_ARRAY({0})",
        "STRING_AGG({0}, ',')", "JSON_ARRAYAGG({0})", "JSON_OBJECTAGG('k': {0})", "CHECKSUM_AGG({0})", "APPROX_COUNT_DISTINCT({0})",
    ];

    /// <summary>A call of each window function inference reads, with <c>{0}</c> where each operand goes in turn.</summary>
    public static readonly string[] Windows =
    [
        "ROW_NUMBER() OVER (ORDER BY id)", "RANK() OVER (ORDER BY id)", "DENSE_RANK() OVER (ORDER BY id)", "NTILE(2) OVER (ORDER BY id)",
        "COUNT({0}) OVER ()", "COUNT_BIG({0}) OVER ()", "SUM({0}) OVER ()", "AVG({0}) OVER (ORDER BY id ROWS BETWEEN 2 PRECEDING AND 1 PRECEDING)",
        "STDEV({0}) OVER ()", "STDEVP({0}) OVER ()", "VAR({0}) OVER ()", "VARP({0}) OVER ()", "MIN({0}) OVER ()",
        "MAX({0}) OVER (PARTITION BY id)", "FIRST_VALUE({0}) OVER (ORDER BY id)", "LAST_VALUE({0}) OVER (ORDER BY id)",
        "LAG({0}) OVER (ORDER BY id)", "LEAD({0}, 1, {0}) OVER (ORDER BY id)",
        "PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY {0}) OVER ()", "PERCENTILE_DISC(0.5) WITHIN GROUP (ORDER BY {0}) OVER ()",
    ];

    /// <summary>Columns and literals, as operands.</summary>
    public static IEnumerable<string> Operands => [.. Columns.Select(c => c.Name), .. Literals];

    /// <summary>The table every expression reads, with a column of each type, never null.</summary>
    public static string Table =>
        $"CREATE TABLE dbo.g (id int NOT NULL PRIMARY KEY, {string.Join(", ", Columns.Select(c => $"{c.Name} {c.Type} NOT NULL"))});";

    /// <summary>
    /// Each column type's values at its edges, as SQL literals: its least and greatest, and 0 and 1 where it holds them, the
    /// empty and the longest string, the first and last instant. The rows of the edge table (<see cref="EdgeRows"/>) mix them.
    /// </summary>
    public static IReadOnlyList<string> Edges(string type) => type switch
    {
        "bit" => ["0", "1"],
        "tinyint" => ["0", "255", "1"],
        "smallint" => ["-32768", "32767", "0", "1"],
        "int" => ["-2147483648", "2147483647", "0", "1"],
        "bigint" => ["-9223372036854775808", "9223372036854775807", "0", "1"],
        "decimal(5,2)" => ["-999.99", "999.99", "0", "1"],
        "decimal(38,0)" => ["-" + new string('9', 38), new string('9', 38), "0", "1"],
        "decimal(38,38)" => ["-0." + new string('9', 38), "0." + new string('9', 38), "0"],
        "numeric(10,2)" => ["-99999999.99", "99999999.99", "0", "1"],
        "money" => ["-922337203685477.5808", "922337203685477.5807", "0", "1"],
        "smallmoney" => ["-214748.3648", "214748.3647", "0", "1"],
        "float" or "float(25)" => ["-1.79E+308", "1.79E+308", "0", "1"],
        "real" or "float(24)" => ["-3.40E+38", "3.40E+38", "0", "1"],
        "char(3)" => ["''", "'abc'", "'1'"],
        "varchar(10)" => ["''", "'abcdefghij'", "'1'"],
        "varchar(max)" => ["''", "REPLICATE(CAST('x' AS varchar(max)), 9000)", "'1'"],
        "nchar(3)" => ["N''", "N'abc'", "N'1'"],
        "nvarchar(10)" => ["N''", "N'abcdefghij'", "N'1'"],
        "nvarchar(max)" => ["N''", "REPLICATE(CAST(N'x' AS nvarchar(max)), 9000)", "N'1'"],
        "date" => ["'0001-01-01'", "'9999-12-31'"],
        "time(3)" => ["'00:00:00'", "'23:59:59.999'"],
        "datetime" => ["'1753-01-01'", "'9999-12-31 23:59:59.997'"],
        "datetime2(3)" => ["'0001-01-01'", "'9999-12-31 23:59:59.999'"],
        "smalldatetime" => ["'1900-01-01'", "'2079-06-06 23:59'"],
        "datetimeoffset(3)" => ["'0001-01-01 00:00:00 -14:00'", "'9999-12-31 23:59:59.999 +14:00'"],
        "uniqueidentifier" => ["'00000000-0000-0000-0000-000000000000'", "'FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF'"],
        "binary(4)" => ["0x00000000", "0xFFFFFFFF"],
        "varbinary(10)" => ["0x", "0xFFFFFFFFFFFFFFFFFFFF"],
        "xml" => ["'<a/>'"],
        "sql_variant" => ["1", "'x'"],
        "json" => ["'{}'", "'[1]'"],
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "No edges for this type."),
    };

    /// <summary>
    /// The edge table's rows, as <c>INSERT</c> statements: one of every column's first edge, one of every column's second, then
    /// rows mixing them so that each value meets the others' least and greatest.
    /// </summary>
    public static IReadOnlyList<string> EdgeRows()
    {
        const int Rows = 24;
        static string ValueOf(int row, int column)
        {
            var edges = Edges(Columns[column].Type);
            var index = row < 2 ? row : ((row * (column + 1)) + column) % edges.Count;
            return edges[Math.Min(index, edges.Count - 1)];
        }

        return [.. Enumerable.Range(0, Rows).Select(r =>
            $"INSERT INTO dbo.g VALUES ({r.ToString(CultureInfo.InvariantCulture)}, {string.Join(", ", Columns.Select((_, c) => ValueOf(r, c)))});")];
    }

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
        "function" => Calls(Functions),
        "window" => Calls(Windows),
        _ => throw new ArgumentOutOfRangeException(nameof(form), form, "Not a form of the sweep."),
    };

    private static List<string> Calls(string[] templates) =>
        [.. templates.SelectMany(t => t.Contains("{0}", StringComparison.Ordinal) ? Operands.Select(o => t.Replace("{0}", o, StringComparison.Ordinal)) : [t])];

    private static List<string> Pairs(string op) => [.. Operands.SelectMany(a => Operands.Select(b => $"{a} {op} {b}"))];

    /// <summary>
    /// Each aggregate over each column, and the counts beside every operand under every operator: an aggregate's result is a
    /// value like any other, and the digits it counts beside a decimal are its own rule. A column beside an aggregate is read
    /// through <c>MIN</c>, which keeps its type, since a query without GROUP BY can't read a column bare.
    /// </summary>
    private static List<string> Aggregates()
    {
        string[] functions = ["COUNT", "COUNT_BIG", "SUM", "AVG", "MIN", "MAX", "STDEV", "STDEVP", "VAR", "VARP"];
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
        var document = project.Infer(project.Procedures).Expected();
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
