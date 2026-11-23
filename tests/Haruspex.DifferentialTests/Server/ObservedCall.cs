using System.Globalization;
using Microsoft.Data.SqlClient;

namespace Haruspex.DifferentialTests.Server;

/// <summary>
/// How the observation tests watch the engine: create what a test needs, make one call, and read every result set as a
/// client does, all in a write session that is rolled back. Shared by <c>StatementFlowObservationTests</c> and
/// <c>ExecObservationTests</c>.
/// </summary>
public static class ObservedCall
{
    /// <summary>
    /// Runs each of <paramref name="setup"/> as a batch of its own (CREATE PROCEDURE must be alone in one), then
    /// <paramref name="call"/>, reading every result set as a client does; rolled back.
    /// </summary>
    /// <param name="server">The fixture, whose write session the whole run holds (it is alone in the database while it lasts).</param>
    /// <param name="setup">Batches run first.</param>
    /// <param name="call">The batch whose results are read.</param>
    /// <param name="errorsAsMessages">
    /// Hand errors of severity 16 and below to the client as messages instead of throwing (SqlClient's
    /// <c>FireInfoMessageEventOnUserErrors</c>), so that what the server does after one can be seen.
    /// </param>
    public static async Task<CallOutcome> Run(SqlServerFixture server, string[] setup, string call, bool errorsAsMessages = false)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(setup);
        server.Require();
        var cancel = TestContext.Current.CancellationToken;

        // It creates procedures and tables, so it is alone in the database while it does (README.md).
        await using var session = await server.WriteAsync(cancel);
        foreach (var batch in setup)
        {
            await using var create = session.Command(batch);
            await create.ExecuteNonQueryAsync(cancel);
        }

        await using var command = session.Command(call);
        return await Read(command, errorsAsMessages, cancel);
    }

    /// <summary>
    /// Runs <paramref name="command"/>, reading every result set as a client does: the outcome <see cref="Run"/> gives, for a
    /// session of a test's own (a <see cref="ScratchDatabase"/>'s).
    /// </summary>
    /// <param name="command">The command, on an open connection.</param>
    /// <param name="errorsAsMessages">As <see cref="Run"/>'s.</param>
    /// <param name="cancel">Cancels the call.</param>
    public static async Task<CallOutcome> Read(SqlCommand command, bool errorsAsMessages, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(command);
        var sets = new List<string>();
        var messages = new List<int>();
        var connection = command.Connection;
        void OnMessage(object sender, SqlInfoMessageEventArgs e) => messages.AddRange(e.Errors.Cast<SqlError>().Where(x => x.Class > 10).Select(x => x.Number));
        connection.InfoMessage += OnMessage;
        connection.FireInfoMessageEventOnUserErrors = errorsAsMessages;
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancel);
            do
            {
                if (reader.FieldCount == 0)
                    continue;
                var names = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName);
                var rows = new List<string>();
                while (await reader.ReadAsync(cancel))
                {
                    rows.Add(string.Join(",", Enumerable.Range(0, reader.FieldCount).Select(i =>
                        reader.IsDBNull(i) ? "null" : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture))));
                }

                sets.Add($"{string.Join(",", names)}: {string.Join(";", rows)}");
            }
            while (await reader.NextResultAsync(cancel));
        }
        catch (SqlException e)
        {
            return new CallOutcome(sets, e.Number, messages);
        }
        finally
        {
            connection.FireInfoMessageEventOnUserErrors = false;
            connection.InfoMessage -= OnMessage;
        }

        return new CallOutcome(sets, null, messages);
    }
}
