using Haruspex.DifferentialTests.Server;
using Haruspex.Ir;

namespace Haruspex.DifferentialTests;

/// <summary>
/// The SQL type the IR gives each column of a procedure's first rowset is the one SQL Server describes for it
/// (<c>sys.dm_exec_describe_first_result_set_for_object</c>). The validator checks values against the IR's kinds and facets,
/// which a wrong precision or length can still pass; this checks the type itself, which the expressions compute
/// (Haruspex.SqlServer.Inference's README.md, "Expressions"). Nullability is not compared: the server's description of it is
/// its own estimate, looser than what haruspex proves (COALESCE is described as nullable whatever its arguments).
/// </summary>
public class DescribedTypeTests(SqlServerFixture server)
{
    public static TheoryData<string> Fixtures { get; } = [.. Runs.Fixtures];

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task Each_typed_rowset_column_has_the_type_sql_server_describes(string fixture)
    {
        await server.RequireAsync();
        var cancel = TestContext.Current.CancellationToken;
        var document = Fixture.Named(fixture).Ir;
        var differences = new List<string>();

        // The server describes procedures from metadata, which a read session's user may not see; nothing is written.
        await using var session = await server.WriteAsync(cancel);
        foreach (var procedure in document.Objects.Where(p => !Runs.NotRun.ContainsKey(p.Id)))
        {
            if (procedure.Results is not [{ Encoding: IrEncoding.Rowset, Type: IrArray { Items: IrRef row } }, ..])
                continue;

            var described = new Dictionary<string, string>(StringComparer.Ordinal);
            await using (var command = session.Command("SELECT name, system_type_name FROM sys.dm_exec_describe_first_result_set_for_object(OBJECT_ID(@name), 0) WHERE name IS NOT NULL;"))
            {
                command.Parameters.AddWithValue("@name", procedure.Id);
                await using var reader = await command.ExecuteReaderAsync(cancel);
                while (await reader.ReadAsync(cancel))
                    described[reader.GetString(0)] = reader.GetString(1);
            }

            // A procedure whose first result set the server cannot describe (temporary tables, branches of other shapes) is
            // left to the value checks.
            if (described.Count == 0)
                continue;

            foreach (var field in document.Types[row.Name].Fields)
            {
                if (field.Type is IrScalar { Sql: { } sql } && described.TryGetValue(field.Name, out var type) && !string.Equals(sql, type, StringComparison.OrdinalIgnoreCase))
                    differences.Add($"{procedure.Id}.{field.Name}: the IR says {sql}, SQL Server describes {type}");
            }
        }

        Assert.True(differences.Count == 0, "Types the IR claims differ from SQL Server's:\n" + string.Join("\n", differences));
    }
}
