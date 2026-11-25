using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Haruspex.DifferentialTests.Server;
using Haruspex.DifferentialTests.Validation;
using Haruspex.SqlServer.Model;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

[assembly: AssemblyFixture(typeof(SqlServerFixture))]

namespace Haruspex.DifferentialTests.Server;

/// <summary>
/// The SQL Server the differential tests run against, with one database for the whole test run, holding every fixture
/// deployed and seeded, whose snapshot reading sessions share, and copies of it that writing sessions take one at a time
/// (<see cref="RoomPool"/>). Nothing is started, created or deployed until a test first needs the server
/// (<see cref="RequireAsync"/>), so a run of tests that need none, such as the validator's, waits for none of it. Where the
/// server comes from, in order (see README.md):
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
    /// direct write through a read session fails with a permission error, and the session cannot leave the snapshot.
    /// </summary>
    private const string Reader = "haruspex_reader";

    /// <summary>
    /// Far longer than any write session lasts (seconds), so only a mistake waits this long for a room: a test holding as many
    /// write sessions as there are rooms and asking for another.
    /// </summary>
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(2);

    private readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyList<ObservedResultSet>>>> observations = new(StringComparer.Ordinal);

    private readonly RoomPool rooms;

    // Everything the fixture sets up, done by the first test that needs the server, once.
    private readonly Lazy<Task> ready;

    // Each database this run creates (the test database, the rooms and the tests' scratch databases) is named here before its CREATE DATABASE is sent, with
    // the CREATE's task, so a drop at any point waits for every CREATE under way and then drops every name. A name whose CREATE
    // failed costs nothing: the drop checks the database exists. The drop is one task, started by whichever comes first of
    // DisposeAsync, SIGTERM and process exit, and awaited by all of them: a process that exited while another path's drop was
    // still running would leave databases behind. Once it has started no room is made.
    private readonly List<(string Name, Task Creation)> creations = [];
    private readonly Lock creating = new();
    private readonly Lazy<Task> drop;
    private bool stopping;

    private MsSqlContainer? container;
    private string? server;
    private string? unavailable;
    private Exception? failure;
    private PosixSignalRegistration? terminate;

    public SqlServerFixture()
    {
        ready = new(StartAsync, LazyThreadSafetyMode.ExecutionAndPublication);
        drop = new(DropAllAsync, LazyThreadSafetyMode.ExecutionAndPublication);

        // As many rooms as tests run at once (xunit's default, a thread a processor), so a write session waits only for a
        // room's first deploy.
        rooms = new(Math.Max(2, Environment.ProcessorCount), Patience, MakeRoomAsync);
    }

    /// <summary>Databases of earlier runs found at the start and less than a day old, so not dropped (see <see cref="DropLeftoversAsync"/>).</summary>
    public IReadOnlyList<string> RecentLeftovers { get; private set; } = [];

    /// <summary>The database created for this run.</summary>
    public string Database { get; } = DatabasePrefix + Guid.NewGuid().ToString("N");

    /// <summary>Whether a test has asked for the server: until one does, nothing is started, created or deployed.</summary>
    internal bool Started => ready.IsValueCreated;

    /// <summary>How many databases write sessions have been given so far (<see cref="RoomPool"/>).</summary>
    internal int Rooms => rooms.Made;

    /// <summary>
    /// A database snapshot of <see cref="Database"/>, taken once it is seeded, which every <see cref="ReadSession"/>
    /// reads. A snapshot is read-only to everyone, so nothing that runs in a read session can write, not even a dbo
    /// procedure that ownership chaining lets past every permission. Its name has the prefix too, and it is dropped
    /// with the database.
    /// </summary>
    private string Snapshot => Database + "_read";

    /// <summary>
    /// A connection string to <see cref="Database"/>, deliberately private: tests reach the databases only through
    /// <see cref="ReadAsync"/> and <see cref="WriteAsync"/>.
    /// </summary>
    private string ConnectionString => Connection(Database);

    // Each kind of session has a pool of its own (a pool is per connection string), so a connection that read as
    // haruspex_reader is never handed to a write session, nor the other way round; and each room has its own.
    private string ReadConnectionString => new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = Snapshot, ApplicationName = "haruspex-tests-read" }.ConnectionString;

    /// <summary>
    /// Nothing: the server is set up by the first test that needs it (<see cref="RequireAsync"/>). Every test in the assembly
    /// shares this fixture, so setting it up here made a run of tests that need no server, such as the validator's, wait for a
    /// container, the deploy and seed of every fixture, and a snapshot.
    /// </summary>
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    /// <summary>
    /// The databases are dropped here, in a <c>finally</c>, whether the tests passed, failed or never got as far as
    /// running. A developer's server is long-running and shared by every run, so a database left behind by each
    /// failed run would pile up, and could be mistaken for a current one. The only case this misses is a process
    /// that is killed with SIGKILL (SIGTERM and process exit drop them too: see <see cref="StartAsync"/>); a
    /// later run names such a database and, after a day, drops it (<see cref="DropLeftoversAsync"/>).
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        terminate?.Dispose();
        try
        {
            // Only databases this run created: when no test asked for the server, or it could not be reached, there is
            // nothing to drop, and the tests already report why.
            if (Created.Count > 0)
            {
                try
                {
                    await drop.Value.ConfigureAwait(false);
                }
                catch (SqlException e)
                {
                    throw new InvalidOperationException($"A test database could not be dropped, so it is still on the server: drop those named {DatabasePrefix}… by hand, once no run is going on. {e.Message}", e);
                }
            }
        }
        finally
        {
            rooms.Dispose();
            if (container is not null)
                await container.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Starts the server, if no test has yet, then skips the calling test if there is no server, or fails it if one is required
    /// or setting up failed. Every way in (<see cref="ReadAsync"/>, <see cref="WriteAsync"/> and the rest) calls it first, so a
    /// test that reaches the server is never ahead of its setup.
    /// </summary>
    public async Task RequireAsync()
    {
        await ready.Value.ConfigureAwait(false);
        if (unavailable is not null)
        {
            if (Environment.GetEnvironmentVariable(RequireVariable) == "1")
                Assert.Fail($"{RequireVariable}=1, but there is no SQL Server: {unavailable}");
            Assert.Skip($"No SQL Server, so the differential tests did not run: {unavailable} Set {ServerVariable} or start Docker; CONTRIBUTING.md says how.");
        }

        if (failure is not null)
            Assert.Fail($"Setting up the test database failed: {failure}");
    }

    /// <summary>Runs a procedure once per test run; later calls reuse what it returned.</summary>
    public async Task<IReadOnlyList<ObservedResultSet>> ObserveAsync(ProcedureRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        await RequireAsync().ConfigureAwait(false);
        return await observations.GetOrAdd(run.Name, _ => new Lazy<Task<IReadOnlyList<ObservedResultSet>>>(
            () => RunAsync(run, CancellationToken.None))).Value.ConfigureAwait(false);
    }

    /// <summary>
    /// Runs a procedure now, not reusing an earlier run: in a <see cref="WriteSession"/>, in a database of its own, when it has
    /// a setup, and otherwise in a <see cref="ReadSession"/>, beside any other session.
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
    /// A session that only reads the test database's snapshot, beside any number of other sessions: nothing writes the test
    /// database once it is seeded, since write sessions have databases of their own. A test that writes through it fails
    /// loudly (see <see cref="ReadSession"/>).
    /// </summary>
    public async Task<ReadSession> ReadAsync(CancellationToken token)
    {
        await RequireAsync().ConfigureAwait(false);
        var connection = new SqlConnection(ReadConnectionString);
        try
        {
            await connection.OpenAsync(token).ConfigureAwait(false);
            // WITH COOKIE, so that only the session, which keeps the cookie, can switch back: a bare REVERT in a test
            // fails. Sent as a plain batch, not through sp_executesql (as a parameterised command would be), whose
            // scope would undo the switch when it ended. EXECUTE AS USER confines the session to the snapshot.
            await using var become = new SqlCommand(
                $"DECLARE @cookie varbinary(8000); EXECUTE AS USER = N'{Reader}' WITH COOKIE INTO @cookie; SELECT @cookie;",
                connection);
            var cookie = (byte[])await become.ExecuteScalarAsync(token).ConfigureAwait(false);
            return new ReadSession(connection, cookie);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// A session that writes, alone in a database of its own, a copy of the test database (a room of <see cref="RoomPool"/>)
    /// that it holds until it is disposed, when everything it did is rolled back and the room goes to the next write session.
    /// Every test that inserts, deletes, creates or alters anything does it here.
    /// </summary>
    public async Task<WriteSession> WriteAsync(CancellationToken token)
    {
        await RequireAsync().ConfigureAwait(false);
        var room = await rooms.EnterAsync(token).ConfigureAwait(false);
        var connection = new SqlConnection(WriteConnectionString(room.Database));
        try
        {
            await connection.OpenAsync(token).ConfigureAwait(false);
            var transaction = (SqlTransaction)await connection.BeginTransactionAsync(token).ConfigureAwait(false);
            return new WriteSession(connection, transaction, room);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            room.Dispose();
            throw;
        }
    }

    /// <summary>
    /// A room held as for a <see cref="WriteSession"/>, for the mssql driver in Node, which connects by itself and rolls back
    /// what it does (<see cref="DriverSession"/>). Its connection is a write session's, as the driver's config.
    /// </summary>
    public async Task<DriverSession> DriverAsync(CancellationToken token)
    {
        await RequireAsync().ConfigureAwait(false);
        var room = await rooms.EnterAsync(token).ConfigureAwait(false);
        try
        {
            return new DriverSession(MssqlConfig(WriteConnectionString(room.Database)), room);
        }
        catch
        {
            room.Dispose();
            throw;
        }
    }

    /// <summary>
    /// A connection string as the mssql driver's config, in JSON. The driver can read a connection string too, but not every
    /// spelling SqlClient's builder writes, so the parts are handed over one by one. Only SQL logins: the test servers
    /// (a container, or the developer's own, CONTRIBUTING.md) use one.
    /// </summary>
    internal static string MssqlConfig(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var source = builder.DataSource.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase) ? builder.DataSource[4..] : builder.DataSource;
        var comma = source.IndexOf(',', StringComparison.Ordinal);
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            server = comma < 0 ? source : source[..comma],
            port = comma < 0 ? 1433 : int.Parse(source[(comma + 1)..], System.Globalization.CultureInfo.InvariantCulture),
            user = builder.UserID,
            password = builder.Password,
            database = builder.InitialCatalog,
            options = new
            {
                encrypt = builder.Encrypt != SqlConnectionEncryptOption.Optional,
                trustServerCertificate = builder.TrustServerCertificate,
                appName = "haruspex-tests-driver",
            },
        });
    }

    /// <summary>
    /// A database of the calling test's own, created now and dropped when it is disposed, whose writes are committed: for
    /// observing what one session sees of another's commits, which the shared databases can't show, since every session in
    /// them is alone and rolled back. It has the shared database's prefix, so one a killed run leaves behind is dropped after
    /// a day as that one is (<see cref="DropLeftoversAsync"/>); nothing else reads it.
    /// </summary>
    /// <param name="collation">The database's collation, such as <c>Latin1_General_100_CS_AS</c>; null for the server's.</param>
    public async Task<ScratchDatabase> ScratchAsync(string? collation = null)
    {
        await RequireAsync().ConfigureAwait(false);
        var name = DatabasePrefix + Guid.NewGuid().ToString("N");
        if (collation is not null && !CollationCatalogue.IsKnown(collation))
            throw new ArgumentException($"{collation} is no collation SQL Server has.", nameof(collation));
        // Named among the run's databases too, so a run stopped by SIGTERM, which never reaches the test's own drop, drops it.
        await CreateAsync(name, collation).ConfigureAwait(false);
        var connection = new SqlConnectionStringBuilder(server) { InitialCatalog = name, Pooling = false, ApplicationName = "haruspex-tests-scratch" };
        return new ScratchDatabase(connection.ConnectionString, () => DropAsync(name));
    }

    /// <summary>Validates a run's output against the IR (<see cref="IrValidator"/>).</summary>
    public async Task<ValidationReport> ValidateAsync(ProcedureRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return IrValidator.Validate(Fixture.Named(run.Fixture).Ir, run.Procedure, await ObserveAsync(run).ConfigureAwait(false));
    }

    /// <summary>
    /// Sets up the server, once, for <see cref="RequireAsync"/>: where it comes from, the leftovers' drop, the test database's
    /// creation, the deploy and seed of every fixture, the reader and the snapshot. A reason not to go on is kept
    /// (<see cref="unavailable"/>, <see cref="failure"/>) and reported to every test that needs the server, rather than failing
    /// the tests that don't.
    /// </summary>
    private async Task StartAsync()
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
                // SshDockerEndpointNotSupportedException from a type initialiser, because Docker.DotNet has no SSH
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
            // `timeout`, `docker stop`), so the databases are also dropped then, and at process exit. The handlers are
            // in place before the first CREATE starts, and wait for every one, so a SIGTERM while one runs cannot leave
            // a database behind. SIGINT (Ctrl+C) cancels the run gracefully and reaches DisposeAsync. Only SIGKILL
            // escapes all three; a later run names what it leaves (RecentLeftovers) and drops it after a day.
            terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, _ => DropOnExit());
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            await CreateAsync(Database).ConfigureAwait(false);

            // Committed, and outside any session, because a fixture's batches are retried after failures, which a
            // transaction would not survive. No session can start until this method has ended.
            await PrepareAsync(ConnectionString).ConfigureAwait(false);

            // Last, so the snapshot holds the seed, the reader and the probe. Its sparse files go beside the
            // database's own data files, wherever the server keeps them.
            await ExecuteOnMasterAsync($"""
                DECLARE @files nvarchar(max) = (
                    SELECT STRING_AGG(CONVERT(nvarchar(max), N'(NAME = ' + QUOTENAME(name) + N', FILENAME = N''' + REPLACE(physical_name, N'''', N'''''') + N'.snapshot'')'), N', ')
                    FROM {Quote(Database)}.sys.database_files WHERE type = 0);
                DECLARE @create nvarchar(max) = N'CREATE DATABASE {Quote(Snapshot)} ON ' + @files + N' AS SNAPSHOT OF {Quote(Database)};';
                EXEC (@create);
                """).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Reported by every test that needs the server (RequireAsync), rather than failing the tests that don't.
        catch (Exception e)
#pragma warning restore CA1031
        {
            failure = e;
        }
    }

    /// <summary>
    /// Deploys and seeds every fixture into the database <paramref name="connectionString"/> names, and adds the reader and,
    /// for IsolationTests, a probe: a dbo procedure that writes, which ownership chaining would let the reader run anywhere
    /// but a snapshot. The probe is outside every fixture's schema, so the IR and coverage never see it. The test database and
    /// every room are made by this one method, so a write session sees what a read session does.
    /// </summary>
    private static async Task PrepareAsync(string connectionString)
    {
        foreach (var fixture in Fixture.All.Where(f => f.IsDeployed))
        {
            await Deployer.DeployAsync(connectionString, fixture.Database).ConfigureAwait(false);
            await Deployer.ExecuteScriptAsync(connectionString, fixture.Seed!).ConfigureAwait(false);
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        foreach (var batch in new[]
        {
            $"CREATE USER {Reader} WITHOUT LOGIN; ALTER ROLE db_datareader ADD MEMBER {Reader}; GRANT EXECUTE TO {Reader};",
            "CREATE TABLE dbo.gate_probe (i int NOT NULL);",
            "CREATE PROCEDURE dbo.gate_probe_write AS SET NOCOUNT ON; INSERT INTO dbo.gate_probe VALUES (1);",
        })
        {
#pragma warning disable CA2100 // The batches are the constant statements above.
            await using var command = new SqlCommand(batch, connection);
#pragma warning restore CA2100
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }

    /// <summary>A room for <see cref="RoomPool"/>: a database of its own, prepared as the test database is.</summary>
    private async Task<string> MakeRoomAsync(CancellationToken token)
    {
        var name = DatabasePrefix + Guid.NewGuid().ToString("N");
        await CreateAsync(name).ConfigureAwait(false);
        await PrepareAsync(Connection(name)).ConfigureAwait(false);
        return name;
    }

    /// <summary>
    /// Creates a database, named first among those to drop, with its CREATE kept for the drop to wait for. Once the drop has
    /// taken its list none is created: the check and the naming are one step, so no CREATE can slip in between the drop's
    /// list and its end (seen on 25 Nov 2026: rooms a SIGTERM caught while being made were left behind).
    /// </summary>
    private async Task CreateAsync(string name, string? collation = null)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (creating)
        {
            if (stopping)
                throw new InvalidOperationException("The test run is stopping, so no database is made.");
            creations.Add((name, done.Task));
        }

        try
        {
            await ExecuteOnMasterAsync($"CREATE DATABASE {Quote(name)}{(collation is null ? "" : " COLLATE " + collation)};").ConfigureAwait(false);
        }
        finally
        {
            done.TrySetResult();
        }
    }

    private string Connection(string database) => new SqlConnectionStringBuilder(server) { InitialCatalog = database }.ConnectionString;

    private string WriteConnectionString(string room) =>
        new SqlConnectionStringBuilder(Connection(room)) { ApplicationName = "haruspex-tests-write" }.ConnectionString;

    private void OnProcessExit(object? sender, EventArgs e) => DropOnExit();

    /// <summary>The databases named so far, whether or not their CREATE has ended.</summary>
    private List<(string Name, Task Creation)> Created
    {
        get
        {
            lock (creating)
                return [.. creations];
        }
    }

    /// <summary>
    /// Drops the databases while the process is being stopped, waiting for it, so the process cannot exit half way.
    /// Best effort: nothing is left to report a failure to but standard error.
    /// </summary>
    private void DropOnExit()
    {
        if (Created.Count == 0)
            return;
        try
        {
            if (!drop.Value.Wait(TimeSpan.FromMinutes(1)))
                Console.Error.WriteLine($"haruspex: dropping the test databases took over a minute; some named {DatabasePrefix}… may be left on the server.");
        }
        catch (AggregateException e)
        {
            Console.Error.WriteLine($"haruspex: the test databases could not all be dropped while stopping: {e.InnerException?.Message}");
        }
    }

    /// <summary>
    /// Drops every database this run created, once no more room can be made: it waits for each CREATE still running, which
    /// would otherwise finish after the drop, then drops each, the test database (and its snapshot) first.
    /// </summary>
    private async Task DropAllAsync()
    {
        List<(string Name, Task Creation)> created;
        lock (creating)
        {
            stopping = true;
            created = [.. creations];
        }

        await Task.WhenAny(Task.WhenAll(created.Select(c => c.Creation)), Task.Delay(TimeSpan.FromSeconds(30))).ConfigureAwait(false);
        SqlException? first = null;
        foreach (var (name, _) in created)
        {
            try
            {
                await DropAsync(name).ConfigureAwait(false);
            }
            catch (SqlException e)
            {
                first ??= e;
            }
        }

        if (first is not null)
            throw first;
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
                "SELECT name, CASE WHEN create_date < DATEADD(day, -1, GETDATE()) THEN 1 ELSE 0 END FROM sys.databases WHERE name LIKE @prefix AND source_database_id IS NULL ORDER BY name;",
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
                // Its snapshot first: a database with a snapshot cannot be dropped. A snapshot is read-only, so it
                // cannot be set to SINGLE_USER; its sessions are killed instead.
                var literal = name.Replace("'", "''", StringComparison.Ordinal);
                await ExecuteOnMasterAsync($"""
                    DECLARE @kill nvarchar(max) = N'', @drop nvarchar(max) = N'';
                    SELECT @kill += N'KILL ' + CAST(s.session_id AS nvarchar(10)) + N';'
                    FROM sys.dm_exec_sessions AS s JOIN sys.databases AS d ON d.database_id = s.database_id
                    WHERE d.source_database_id = DB_ID(N'{literal}') AND s.session_id <> @@SPID;
                    SELECT @drop += N'DROP DATABASE ' + QUOTENAME(name) + N';' FROM sys.databases WHERE source_database_id = DB_ID(N'{literal}');
                    EXEC (@kill);
                    EXEC (@drop);
                    IF DB_ID(N'{literal}') IS NOT NULL BEGIN ALTER DATABASE {Quote(name)} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {Quote(name)}; END;
                    """).ConfigureAwait(false);
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
