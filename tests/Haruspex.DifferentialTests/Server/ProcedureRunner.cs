using System.Data;
using Haruspex.DifferentialTests.Validation;
using Microsoft.Data.SqlClient;

namespace Haruspex.DifferentialTests.Server;

internal static class ProcedureRunner
{
    /// <summary>
    /// Runs a procedure and reads every result set it returns. A run with <see cref="ProcedureRun.Setup"/> runs
    /// it first in a transaction that is rolled back afterwards, so a run can see the tables in a state the seed
    /// does not leave them in (empty, say) without changing what other runs see.
    /// </summary>
    public static async Task<IReadOnlyList<ObservedResultSet>> RunAsync(string connectionString, ProcedureRun run)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = run.Setup is null ? null : (SqlTransaction)await connection.BeginTransactionAsync().ConfigureAwait(false);
        try
        {
            if (run.Setup is not null)
            {
#pragma warning disable CA2100 // The run's own setup SQL, from the repository.
                await using var setup = new SqlCommand(run.Setup, connection, transaction);
#pragma warning restore CA2100
                await setup.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            await using var command = new SqlCommand(run.Procedure, connection, transaction) { CommandType = CommandType.StoredProcedure };
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
        finally
        {
            if (transaction is not null)
                await transaction.RollbackAsync().ConfigureAwait(false);
        }
    }

    private static object? Read(SqlDataReader reader, int i)
    {
        if (reader.IsDBNull(i))
            return null;
        try
        {
            return reader.GetValue(i);
        }
        catch (OverflowException)
        {
            // A decimal with more digits than System.Decimal holds (decimal(38, 0) can): read it exactly instead.
            return reader.GetSqlDecimal(i);
        }
    }
}
