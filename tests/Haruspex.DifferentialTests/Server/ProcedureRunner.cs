using System.Data;
using System.Data.Common;
using System.Globalization;
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
        foreach (var (name, argument) in run.Parameters)
            command.Parameters.Add(argument.Parameter(name));

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
            var schema = await reader.GetColumnSchemaAsync().ConfigureAwait(false);
            sets.Add(new ObservedResultSet(columns, rows) { ColumnTypes = [.. schema.Select(SqlTypeText)] });
        }
        while (await reader.NextResultAsync().ConfigureAwait(false));

        return sets;
    }

    /// <summary>
    /// A column's SQL type as it would be written, with its precision, scale or length (<c>decimal(19,2)</c>, <c>time(7)</c>,
    /// <c>nvarchar(40)</c>), from what the driver reports of it; the type's name alone for the types that take none.
    /// </summary>
    private static string SqlTypeText(DbColumn column)
    {
        var name = column.DataTypeName ?? throw new InvalidOperationException($"The column {column.ColumnName} has no type name.");
        return name switch
        {
            "decimal" or "numeric" => $"{name}({column.NumericPrecision},{column.NumericScale})",
            "time" or "datetime2" or "datetimeoffset" => $"{name}({column.NumericScale})",
            "char" or "varchar" or "nchar" or "nvarchar" or "binary" or "varbinary" =>
                $"{name}({(column.ColumnSize is null or int.MaxValue or -1 ? "max" : column.ColumnSize.Value.ToString(CultureInfo.InvariantCulture))})",
            _ => name,
        };
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
            // throws SqlTypeException rather than OverflowException for some (1 as decimal(38,37)).
            return reader.GetSqlDecimal(i);
        }
    }
}
