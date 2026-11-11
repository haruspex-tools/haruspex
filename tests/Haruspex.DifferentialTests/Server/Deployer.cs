using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Haruspex.DifferentialTests.Server;

/// <summary>Deploys a fixture's <c>.sql</c> files, and runs seed scripts, batch by batch.</summary>
internal static partial class Deployer
{
    /// <summary>
    /// Runs every batch of every <c>.sql</c> file under <paramref name="directory"/>. A fixture is a folder of
    /// scripts in no particular order, like a database project, so a batch that fails (a table whose foreign key
    /// names a table not created yet) is retried after the others, until a pass makes no progress. Procedures
    /// never need this: SQL Server resolves the tables they read when they first run.
    /// </summary>
    public static async Task DeployAsync(string connectionString, string directory)
    {
        var pending = Directory.EnumerateFiles(directory, "*.sql", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .SelectMany(file => Batches(File.ReadAllText(file)).Select(batch => (File: Path.GetRelativePath(directory, file), Batch: batch)))
            .ToList();

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        while (pending.Count > 0)
        {
            var failed = new List<(string File, string Batch, SqlException Error)>();
            foreach (var (file, batch) in pending)
            {
                try
                {
                    await ExecuteAsync(connection, batch).ConfigureAwait(false);
                }
                catch (SqlException e)
                {
                    failed.Add((file, batch, e));
                }
            }

            if (failed.Count == pending.Count)
                throw new InvalidOperationException($"Deploying {directory} failed:\n" + string.Join("\n", failed.Select(f => $"{f.File}: {f.Error.Message}")));
            pending = [.. failed.Select(f => (f.File, f.Batch))];
        }
    }

    /// <summary>Runs a script's batches in order, stopping at the first error.</summary>
    public static async Task ExecuteScriptAsync(string connectionString, string path)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        foreach (var batch in Batches(await File.ReadAllTextAsync(path).ConfigureAwait(false)))
            await ExecuteAsync(connection, batch).ConfigureAwait(false);
    }

    private static async Task ExecuteAsync(SqlConnection connection, string batch)
    {
#pragma warning disable CA2100 // Fixture and seed scripts are the repository's own SQL: running them is the point.
        await using var command = new SqlCommand(batch, connection);
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    /// <summary>Splits a script on <c>GO</c> lines, as sqlcmd and SSMS do; <c>GO</c> is not T-SQL.</summary>
    internal static IEnumerable<string> Batches(string script) =>
        GoLine().Split(script).Where(b => !string.IsNullOrWhiteSpace(b));

    [GeneratedRegex(@"^[ \t]*GO[ \t]*(?:--[^\r\n]*)?\r?$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex GoLine();
}
