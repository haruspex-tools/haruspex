using System.Collections.Concurrent;
using Haruspex.DifferentialTests.Server;
using Haruspex.DifferentialTests.Validation;
using Haruspex.Ir;
using Haruspex.SqlServer;
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

    private readonly ConcurrentDictionary<string, Lazy<IrDocument>> documents = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Lazy<Task<ValidationReport>>> reports = new(StringComparer.Ordinal);

    private MsSqlContainer? container;
    private string? server;
    private string? unavailable;
    private Exception? failure;

    /// <summary>The database created for this run.</summary>
    public string Database { get; } = DatabasePrefix + Guid.NewGuid().ToString("N");

    /// <summary>A connection string to <see cref="Database"/>.</summary>
    public string ConnectionString => new SqlConnectionStringBuilder(server) { InitialCatalog = Database }.ConnectionString;

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
            await DropLeftoversAsync().ConfigureAwait(false);
            await ExecuteOnMasterAsync($"CREATE DATABASE {Quote(Database)};").ConfigureAwait(false);
            foreach (var fixture in Runs.Fixtures)
            {
                await Deployer.DeployAsync(ConnectionString, Paths.Fixture(fixture)).ConfigureAwait(false);
                await Deployer.ExecuteScriptAsync(ConnectionString, Paths.Seed(fixture)).ConfigureAwait(false);
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
    /// that is killed; <see cref="DropLeftoversAsync"/> deals with those on a later run.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (server is not null && unavailable is null)
                await DropAsync(Database).ConfigureAwait(false);
        }
        finally
        {
            if (container is not null)
                await container.DisposeAsync().ConfigureAwait(false);
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

    /// <summary>Runs a procedure once per test run and validates its output; later calls reuse the report.</summary>
    public Task<ValidationReport> ValidateAsync(ProcedureRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return reports.GetOrAdd(run.Name, _ => new Lazy<Task<ValidationReport>>(async () =>
        {
            var observed = await ProcedureRunner.RunAsync(ConnectionString, run).ConfigureAwait(false);
            return IrValidator.Validate(Ir(run.Fixture), run.Procedure, observed);
        })).Value;
    }

    /// <summary>
    /// Drops databases left by earlier runs whose process was killed before it could drop them. Only ones more
    /// than a day old, so a run going on at the same time against the same server keeps its database.
    /// </summary>
    private async Task DropLeftoversAsync()
    {
        var leftovers = new List<string>();
        await using (var connection = new SqlConnection(server))
        {
            await connection.OpenAsync().ConfigureAwait(false);
            await using var command = new SqlCommand(
                "SELECT name FROM sys.databases WHERE name LIKE @prefix AND create_date < DATEADD(day, -1, GETDATE());",
                connection);
            command.Parameters.AddWithValue("@prefix", DatabasePrefix.Replace("_", "[_]", StringComparison.Ordinal) + "%");
            await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
                leftovers.Add(reader.GetString(0));
        }

        foreach (var name in leftovers)
            await DropAsync(name).ConfigureAwait(false);
    }

    private async Task DropAsync(string name)
    {
        // Pooled connections to the database would block the drop.
        SqlConnection.ClearAllPools();
        await ExecuteOnMasterAsync(
            $"IF DB_ID(N'{name.Replace("'", "''", StringComparison.Ordinal)}') IS NOT NULL BEGIN ALTER DATABASE {Quote(name)} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {Quote(name)}; END;")
            .ConfigureAwait(false);
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
