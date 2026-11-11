using System.Data;
using Haruspex.DifferentialTests.Validation;
using Microsoft.Data.SqlClient;

namespace Haruspex.DifferentialTests.Server;

internal static class ProcedureRunner
{
    /// <summary>Runs a procedure that only reads, and reads every result set it returns.</summary>
    public static Task<IReadOnlyList<ObservedResultSet>> RunAsync(ReadSession session, ProcedureRun run)
    {
        if (run.Setup is not null)
            throw new ArgumentException($"The run {run.Name} has a setup, which writes, so it needs a write session.", nameof(run));
        return ReadAllAsync(session, run);
    }

    /// <summary>
    /// Runs <see cref="ProcedureRun.Setup"/>, then the procedure, and reads every result set it returns. The session
    /// rolls back when it is disposed, so a run can see the tables in a state the seed does not leave them in (empty,
    /// say) without changing what other runs see; it is alone in the database while it lasts, so no run reading the
    /// same tables waits on its locks (see <see cref="DatabaseGate"/>).
    /// </summary>
    public static async Task<IReadOnlyList<ObservedResultSet>> RunAsync(WriteSession session, ProcedureRun run)
    {
        if (run.Setup is not null)
        {
            await using var setup = session.Command(run.Setup);
            await setup.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        return await ReadAllAsync(session, run).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<ObservedResultSet>> ReadAllAsync(DatabaseSession session, ProcedureRun run)
    {
        await using var command = session.Command(run.Procedure);
        command.CommandType = CommandType.StoredProcedure;
        foreach (var (name, value) in run.Parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);

        var sets = new List<ObservedResultSet>();
        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        do
        {
            // A statement that returns no result set (an INSERT, say) has no columns; it is not a result.
            if (reader.FieldCount == 0)
                continue;

            var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
            var rows = new List<IReadOnlyList<object?>>();
            while (await reader.ReadAsync().ConfigureAwait(false))
                rows.Add([.. Enumerable.Range(0, reader.FieldCount).Select(i => Read(reader, i))]);
            sets.Add(new ObservedResultSet(columns, rows));
        }
        while (await reader.NextResultAsync().ConfigureAwait(false));

        return sets;
    }

    private static object? Read(SqlDataReader reader, int i)
    {
        if (reader.IsDBNull(i))
            return null;
        try
        {
            return reader.GetValue(i);
        }
        catch (Exception e) when (e is OverflowException or System.Data.SqlTypes.SqlTypeException)
        {
            // A decimal with more digits than System.Decimal holds (decimal(38, 0) can): read it exactly instead. SqlClient
            // throws SqlTypeException rather than OverflowException for some (1 as decimal(38,37), slice 6j).
            return reader.GetSqlDecimal(i);
        }
    }
}
