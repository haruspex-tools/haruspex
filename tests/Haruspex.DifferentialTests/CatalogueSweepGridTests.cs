using System.Globalization;
using System.Text;
using Haruspex.DifferentialTests.Server;
using Haruspex.SqlServer.Inference;
using Haruspex.SqlServer.Semantics;

namespace Haruspex.DifferentialTests;

/// <summary>
/// Every expression of the sweep (<see cref="CatalogueSweep"/>: each built-in type through arithmetic, a sign, <c>CAST</c>,
/// <c>ISNULL</c> and the aggregates) that haruspex types has the type SQL Server describes for the same query. So the unit
/// tests' snapshot of the sweep, <c>CatalogueSweepTests</c>, holds only what the server does, for every type, not only those a
/// fixture reaches. An expression haruspex leaves unknown claims nothing and is not compared; one the server refuses is
/// unknown in haruspex too, except over a type inference doesn't model (<see cref="ReadsAnUnmodelledType"/>).
/// </summary>
/// <remarks>
/// This checks the type, which is the server's to describe. Why a value may be null, and the facets, are not: the server
/// describes no reasons, and the facets of a type are its range and digits, which <c>BuiltInTypeObservationTests</c> observes
/// type by type. The reasons rest on the observation tests each rule names.
/// </remarks>
public class CatalogueSweepGridTests(SqlServerFixture server)
{
    public static TheoryData<string> Forms { get; } = [.. CatalogueSweep.Forms];

    [Theory]
    [MemberData(nameof(Forms))]
    public async Task Each_expression_haruspex_types_has_the_type_sql_server_describes(string form)
    {
        server.Require();
        var outcomes = CatalogueSweep.Infer(form);
        var typed = outcomes.Where(o => o.Sql is not null).ToList();
        var described = await Describe([.. typed.Select(o => CatalogueSweep.Query(o.Expression))]);

        var differences = typed
            .Select((o, i) => (o.Expression, Haruspex: o.Sql, Server: described[i]))
            .Where(d => !SameType(d.Haruspex, d.Server))
            .Where(d => !(d.Server.StartsWith('!') && ReadsAnUnmodelledType(d.Expression)))
            .Select(d => $"{d.Expression}: haruspex {d.Haruspex}, SQL Server {d.Server}")
            .ToList();
        Assert.True(differences.Count == 0, $"{differences.Count} of {typed.Count} typed expressions differ:\n" + string.Join("\n", differences));
    }

    /// <summary>
    /// Whether haruspex's type and the server's are one: the IR writes a column's type as declared (<c>float(24)</c>), and the
    /// server describes the type it is (<c>real</c>), so the two are compared as types, and as text only where either names none.
    /// </summary>
    private static bool SameType(string? haruspex, string server) =>
        ScalarType.Parse(haruspex) is { } a && ScalarType.Parse(server) is { } b ? a == b : string.Equals(haruspex, server, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether an expression reads a column of a type inference doesn't model (binary, <c>xml</c>, <c>sql_variant</c>,
    /// <c>json</c>). Its value is unknown, so haruspex can't tell that the server refuses an expression over it, such as
    /// <c>ISNULL(i, x)</c>, whose second argument doesn't convert to int; when the first argument is never NULL it is that
    /// argument, which is right for every expression the server accepts. A refused one is in no procedure the server
    /// created, so what haruspex says of it claims nothing, and it is the one difference not counted.
    /// </summary>
    private static bool ReadsAnUnmodelledType(string expression) =>
        CatalogueSweep.Columns
            .Where(c => ScalarType.Parse(c.Type) is not { } type || !SqlTypes.IsModelled(type.Type))
            .Any(c => System.Text.RegularExpressions.Regex.IsMatch(expression, $@"\b{c.Name}\b"));

    /// <summary>
    /// The type SQL Server describes for each query's one column, or <c>!</c> and the error it gives: each described on its own,
    /// since an error fails its whole query, in one batch.
    /// </summary>
    private async Task<string[]> Describe(List<string> queries)
    {
        var batch = new StringBuilder("SET NOCOUNT ON; DECLARE @r TABLE (n int NOT NULL, t nvarchar(200) NULL, e int NULL);\n");
        for (var i = 0; i < queries.Count; i++)
        {
            var query = queries[i].Replace("'", "''", StringComparison.Ordinal);
            batch.Append(CultureInfo.InvariantCulture, $"INSERT INTO @r SELECT TOP (1) {i}, system_type_name, error_number FROM sys.dm_exec_describe_first_result_set(N'{query}', NULL, 0) ORDER BY column_ordinal;\n");
        }

        batch.Append("SELECT CONCAT(n, ' ', ISNULL(t, CONCAT('!', e))) AS c FROM @r ORDER BY n;");
        var outcome = await ObservedCall.Run(server, [CatalogueSweep.Table], batch.ToString());
        Assert.Null(outcome.Error);
        var types = new string[queries.Count];
        if (queries.Count == 0)
            return types;
        foreach (var line in outcome.Sets[0]["c: ".Length..].Split(';'))
        {
            var space = line.IndexOf(' ', StringComparison.Ordinal);
            types[int.Parse(line[..space], CultureInfo.InvariantCulture)] = line[(space + 1)..];
        }

        return types;
    }
}
