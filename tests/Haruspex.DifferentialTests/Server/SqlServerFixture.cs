using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Haruspex.DifferentialTests.Server;
using Haruspex.DifferentialTests.Validation;
using Haruspex.Ir;
using Haruspex.Cli;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

[assembly: AssemblyFixture(typeof(SqlServerFixture))]

namespace Haruspex.DifferentialTests.Server;

/// <summary>
/// The SQL Server the differential tests run against, with one database for the whole test run, holding every
/// fixture deployed and seeded. Where the server comes from, in order (see README.md):
/// <list type="number">
/// <item><c>HARUSPEX_TEST_SQLSERVER</c>, a connection string to a server of the developer's own;</item>
/// <item>a container started by Testcontainers, from the pinned <see cref="Image"/>;</item>
/// <item>neither: every test that needs a server is skipped, saying why, unless
/// <c>HARUSPEX_REQUIRE_SQLSERVER=1</c> (set in CI) turns the skip into a failure.</item>
/// </list>
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    /// <summary>
    /// Pinned by tag and digest, so a run is repeatable and an image update is a reviewed change. SQL Server 2025
    /// matches the grammar the front end parses with (TSql170Parser).
    /// </summary>
    public const string Image =
        "mcr.microsoft.com/mssql/server:2025-CU10-ubuntu-24.04@sha256:245f6b1c8903edae52cb20fa31419cefcde1a49938bb8c3447099011fc871ca3";

    public const string ServerVariable = "HARUSPEX_TEST_SQLSERVER";
    public const string RequireVariable = "HARUSPEX_REQUIRE_SQLSERVER";

    /// <summary>The prefix of every database the tests create, so a leftover one is recognisable.</summary>
    private const string DatabasePrefix = "haruspex_test_";

    /// <summary>
    /// The database user every <see cref="ReadSession"/> runs as: it may read and execute, and nothing more, so a
    /// write through a read session fails at once with a permission error (229 or 262), whatever the test.
    /// </summary>
    private const string Reader = "haruspex_reader";

    private readonly DatabaseGate gate = new();

    private readonly ConcurrentDictionary<string, Lazy<IrDocument>> documents = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyList<ObservedResultSet>>>> observations = new(StringComparer.Ordinal);

    private MsSqlContainer? container;
    private string? server;
    private string? unavailable;
    private Exception? failure;
    private PosixSignalRegistration? terminate;

    // Completes when the CREATE DATABASE, once started, has ended either way; created is 1 once it has succeeded. The drop is one task, started by whichever comes first of DisposeAsync,
    // SIGTERM and process exit, and awaited by all of them: a process that exited while another path's drop was
    // still running would leave the database behind.
    private Task? creation;
    private int created;
    private readonly Lazy<Task> drop;

    public SqlServerFixture() => drop = new(() => DropAsync(Database), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Databases of earlier runs found at the start and less than a day old, so not dropped (see <see cref="DropLeftoversAsync"/>).</summary>
    public IReadOnlyList<string> RecentLeftovers { get; private set; } = [];

    /// <summary>The database created for this run.</summary>
    public string Database { get; } = DatabasePrefix + Guid.NewGuid().ToString("N");

    /// <summary>
    /// A connection string to <see cref="Database"/>, deliberately private: tests reach the database only through
    /// <see cref="ReadAsync"/> and <see cref="WriteAsync"/>, which go through the <see cref="DatabaseGate"/>.
    /// </summary>
    private string ConnectionString => new SqlConnectionStringBuilder(server) { InitialCatalog = Database }.ConnectionString;

    // Each kind of session has a pool of its own (a pool is per connection string), so a connection that read as
    // haruspex_reader is never handed to a write session, nor the other way round.
    private string ReadConnectionString => new SqlConnectionStringBuilder(ConnectionString) { ApplicationName = "haruspex-tests-read" }.ConnectionString;

    private string WriteConnectionString => new SqlConnectionStringBuilder(ConnectionString) { ApplicationName = "haruspex-tests-write" }.ConnectionString;

    public async ValueTask InitializeAsync()
    {
        server = Environment.GetEnvironmentVariable(ServerVariable);
        if (string.IsNullOrWhiteSpace(server))
        {
            try
            {
                container = new MsSqlBuilder(Image).Build();
                await container.StartAsync().ConfigureAwait(false);
                server = container.GetConnectionString();
            }
#pragma warning disable CA1031 // Any reason Docker cannot be used is a reason to skip; HARUSPEX_REQUIRE_SQLSERVER turns it into a failure.
            catch (Exception e)
#pragma warning restore CA1031
            {
                // Not only DockerUnavailableException: an ssh:// Docker host (a Docker context over SSH) throws
                // SshDockerEndpointNotSupportedException from a type initializer, because Docker.DotNet has no SSH
                // transport. Observed with Testcontainers 4.16.0; see README.md.
                unavailable = $"{ServerVariable} is not set, and Testcontainers could not start SQL Server ({Innermost(e).GetType().Name}: {Innermost(e).Message}).";
                server = null;
                return;
            }
        }

        try
        {
            RecentLeftovers = await DropLeftoversAsync().ConfigureAwait(false);

            // DisposeAsync is not always reached when the process is stopped by SIGTERM (an IDE's stop button,
            // `timeout`, `docker stop`), so the database is also dropped then, and at process exit. The handlers are
            // in place before the CREATE starts, and wait for it, so a SIGTERM while it runs cannot leave the
            // database behind. SIGINT (Ctrl+C) cancels the run gracefully and reaches DisposeAsync. Only SIGKILL
            // escapes all three; a later run names what it leaves (RecentLeftovers) and drops it after a day.
            terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, _ => DropOnExit());
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            // Published before the CREATE is sent, and completed however it ends, so a SIGTERM at any point after
            // this line waits for it.
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref creation, done.Task);
            try
            {
                await ExecuteOnMasterAsync($"CREATE DATABASE {Quote(Database)};").ConfigureAwait(false);
                Volatile.Write(ref created, 1);
            }
            finally
            {
                done.TrySetResult();
            }

            // The one write that is committed, and the only one outside a WriteSession, because a fixture's batches
            // are retried after failures, which a transaction would not survive. It holds the gate all the same,
            // though no test has started yet, so that every write goes through it.
            using (await gate.EnterWriteAsync(CancellationToken.None).ConfigureAwait(false))
            {
                foreach (var fixture in Runs.Fixtures)
                {
                    await Deployer.DeployAsync(ConnectionString, Paths.Fixture(fixture)).ConfigureAwait(false);
                    await Deployer.ExecuteScriptAsync(ConnectionString, Paths.Seed(fixture)).ConfigureAwait(false);
                }

                await using var connection = new SqlConnection(ConnectionString);
                await connection.OpenAsync().ConfigureAwait(false);
                await using var command = new SqlCommand(
                    $"CREATE USER {Reader} WITHOUT LOGIN; ALTER ROLE db_datareader ADD MEMBER {Reader}; GRANT EXECUTE TO {Reader};",
                    connection);
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }
#pragma warning disable CA1031 // Reported by every test that needs the server (Require), rather than failing the tests that don't.
        catch (Exception e)
#pragma warning restore CA1031
        {
            failure = e;
        }
    }

    /// <summary>
    /// The database is dropped here, in a <c>finally</c>, whether the tests passed, failed or never got as far as
    /// running. A developer's server is long-running and shared by every run, so a database left behind by each
    /// failed run would pile up, and could be mistaken for a current one. The only case this misses is a process
    /// that is killed with SIGKILL (SIGTERM and process exit drop it too: see <see cref="InitializeAsync"/>); a
    /// later run names such a database and, after a day, drops it (<see cref="DropLeftoversAsync"/>).
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        terminate?.Dispose();
        try
        {
            // Only a database this run created: when the server could not be reached there is nothing to drop,
            // and the tests already report why.
            if (Volatile.Read(ref created) == 1)
            {
                try
                {
                    await drop.Value.ConfigureAwait(false);
                }
                catch (SqlException e)
                {
                    throw new InvalidOperationException($"The test database {Database} could not be dropped, so it is still on the server: drop it by hand. {e.Message}", e);
                }
            }
        }
        finally
        {
            gate.Dispose();
            if (container is not null)
                await container.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void OnProcessExit(object? sender, EventArgs e) => DropOnExit();

    /// <summary>
    /// Drops the database while the process is being stopped, waiting for it, so the process cannot exit half way.
    /// Best effort: nothing is left to report a failure to but standard error.
    /// </summary>
    private void DropOnExit()
    {
        if (Volatile.Read(ref creation) is not { } started)
            return;
        try
        {
            // A CREATE still running would otherwise finish after the drop. The drop checks the database exists, so
            // a CREATE that failed costs nothing.
            started.Wait(TimeSpan.FromSeconds(30));
            if (!drop.Value.Wait(TimeSpan.FromSeconds(30)))
                Console.Error.WriteLine($"haruspex: dropping the test database {Database} took over 30 seconds; it may be left on the server.");
        }
        catch (AggregateException e)
        {
            Console.Error.WriteLine($"haruspex: the test database {Database} could not be dropped while stopping: {e.InnerException?.Message}");
        }
    }

    /// <summary>Skips the calling test if there is no server, or fails it if one is required or setting up failed.</summary>
    public void Require()
    {
        if (unavailable is not null)
        {
            if (Environment.GetEnvironmentVariable(RequireVariable) == "1")
                Assert.Fail($"{RequireVariable}=1, but there is no SQL Server: {unavailable}");
            Assert.Skip($"No SQL Server, so the differential tests did not run: {unavailable} Set {ServerVariable} or start Docker; CONTRIBUTING.md says how.");
        }

        if (failure is not null)
            Assert.Fail($"Setting up the test database failed: {failure}");
    }

    /// <summary>The IR inferred for every procedure of a fixture.</summary>
    public IrDocument Ir(string fixture) => documents.GetOrAdd(fixture, f => new Lazy<IrDocument>(() =>
    {
        var project = SqlProject.Load(Paths.Fixture(f));
        if (project.Diagnostics.Count > 0)
            throw new InvalidOperationException($"The fixture {f} has diagnostics: {string.Join("; ", project.Diagnostics)}");
        return project.Infer(project.Procedures);
    })).Value;

    /// <summary>Runs a procedure once per test run; later calls reuse what it returned.</summary>
    public Task<IReadOnlyList<ObservedResultSet>> ObserveAsync(ProcedureRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return observations.GetOrAdd(run.Name, _ => new Lazy<Task<IReadOnlyList<ObservedResultSet>>>(
            () => RunAsync(run, CancellationToken.None))).Value;
    }

    /// <summary>
    /// Runs a procedure now, not reusing an earlier run: in a <see cref="WriteSession"/>, alone, when it has a setup,
    /// and otherwise in a <see cref="ReadSession"/>, beside any other reader.
    /// </summary>
    public async Task<IReadOnlyList<ObservedResultSet>> RunAsync(ProcedureRun run, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Setup is null)
        {
            await using var read = await ReadAsync(token).ConfigureAwait(false);
            return await ProcedureRunner.RunAsync(read, run).ConfigureAwait(false);
        }

        await using var write = await WriteAsync(token).ConfigureAwait(false);
        return await ProcedureRunner.RunAsync(write, run).ConfigureAwait(false);
    }

    /// <summary>
    /// A session that only reads, beside any number of others, once no write session is open. It runs as a database
    /// user that cannot write, so a test that writes through it fails loudly. Hold one session at a time: the gate is
    /// not re-entrant, and a second request waits for the first to end (see <see cref="DatabaseGate.Patience"/>).
    /// </summary>
    public async Task<ReadSession> ReadAsync(CancellationToken token)
    {
        var pass = await gate.EnterReadAsync(token).ConfigureAwait(false);
        var connection = new SqlConnection(ReadConnectionString);
        try
        {
            await connection.OpenAsync(token).ConfigureAwait(false);
            // A pooled connection may still be impersonating from its last use, or reset; either way it ends up as
            // the reader. EXECUTE AS USER is scoped to this database, where the reader is a user.
            await using var become = new SqlCommand($"IF USER_NAME() <> N'{Reader}' EXECUTE AS USER = N'{Reader}';", connection);
            await become.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            return new ReadSession(connection, pass);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            pass.Dispose();
            throw;
        }
    }

    /// <summary>
    /// A session that writes, alone in the database: it waits for every open session to end, and none starts until
    /// it is disposed, which rolls back everything it did. Every test that inserts, deletes, creates or alters
    /// anything in the test database does it here.
    /// </summary>
    public async Task<WriteSession> WriteAsync(CancellationToken token)
    {
        var pass = await gate.EnterWriteAsync(token).ConfigureAwait(false);
        var connection = new SqlConnection(WriteConnectionString);
        try
        {
            await connection.OpenAsync(token).ConfigureAwait(false);
            var transaction = (SqlTransaction)await connection.BeginTransactionAsync(token).ConfigureAwait(false);
            return new WriteSession(connection, transaction, pass);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            pass.Dispose();
            throw;
        }
    }

    /// <summary>Validates a run's output against the IR (<see cref="IrValidator"/>).</summary>
    public async Task<ValidationReport> ValidateAsync(ProcedureRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return IrValidator.Validate(Ir(run.Fixture), run.Procedure, await ObserveAsync(run).ConfigureAwait(false));
    }

    /// <summary>
    /// Drops databases left by earlier runs whose process was killed before it could drop them. Only ones more
    /// than a day old, so a run going on at the same time against the same server keeps its database. Returns the
    /// younger ones, which the tests name as a warning so a leftover is noticed at once.
    /// </summary>
    private async Task<IReadOnlyList<string>> DropLeftoversAsync()
    {
        var leftovers = new List<string>();
        var recent = new List<string>();
        await using (var connection = new SqlConnection(server))
        {
            await connection.OpenAsync().ConfigureAwait(false);
            await using var command = new SqlCommand(
                "SELECT name, CASE WHEN create_date < DATEADD(day, -1, GETDATE()) THEN 1 ELSE 0 END FROM sys.databases WHERE name LIKE @prefix ORDER BY name;",
                connection);
            command.Parameters.AddWithValue("@prefix", DatabasePrefix.Replace("_", "[_]", StringComparison.Ordinal) + "%");
            await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
                (reader.GetInt32(1) == 1 ? leftovers : recent).Add(reader.GetString(0));
        }

        foreach (var name in leftovers)
            await DropAsync(name).ConfigureAwait(false);
        return recent;
    }

    private async Task DropAsync(string name)
    {
        // Tests may still be running when SIGTERM starts the drop, and one of them can take the single user's place
        // between the ALTER and the DROP (the dropping connection is in master, not in the database), which makes
        // the DROP fail. Each attempt rolls them back again, so a few attempts are enough.
        for (var attempt = 1; ; attempt++)
        {
            // Pooled connections to the database would block the drop.
            SqlConnection.ClearAllPools();
            try
            {
                await ExecuteOnMasterAsync(
                    $"IF DB_ID(N'{name.Replace("'", "''", StringComparison.Ordinal)}') IS NOT NULL BEGIN ALTER DATABASE {Quote(name)} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {Quote(name)}; END;")
                    .ConfigureAwait(false);
                return;
            }
            catch (SqlException) when (attempt < 5)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt)).ConfigureAwait(false);
            }
        }
    }

    private async Task ExecuteOnMasterAsync(string sql)
    {
        await using var connection = new SqlConnection(new SqlConnectionStringBuilder(server) { InitialCatalog = "master" }.ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);
#pragma warning disable CA2100 // The only text built in is a database name this class generated, quoted.
        await using var command = new SqlCommand(sql, connection);
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static Exception Innermost(Exception e) => e.InnerException is null ? e : Innermost(e.InnerException);

    private static string Quote(string name) => "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";
}
