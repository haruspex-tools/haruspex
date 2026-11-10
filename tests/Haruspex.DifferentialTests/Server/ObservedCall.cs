using System.Globalization;
using Microsoft.Data.SqlClient;

namespace Haruspex.DifferentialTests.Server;

/// <summary>What a call returned: each result set as <c>columns: rows</c>, and the error that ended it, if any.</summary>
/// <param name="Sets">Each result set a client received, in order.</param>
/// <param name="Error">The number of the error the call failed with, or null when it succeeded.</param>
/// <param name="Messages">Errors handed to the client as messages (see <see cref="ObservedCall.Run"/>), by number.</param>
public sealed record CallOutcome(IReadOnlyList<string> Sets, int? Error, IReadOnlyList<int> Messages)
{
    public override string ToString() =>
        string.Join(" | ", Sets) + (Error is { } e ? $" ! {e}" : "") + (Messages.Count > 0 ? " ~ " + string.Join(",", Messages) : "");
}

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

        var sets = new List<string>();
        var messages = new List<int>();
        await using var command = session.Command(call);
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
