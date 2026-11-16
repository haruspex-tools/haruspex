namespace Haruspex.DifferentialTests.Server;

/// <summary>
/// The test database held alone for a program outside this process, the mssql driver in Node (<see cref="MssqlDriver"/>),
/// which can't share a session's connection. It holds the gate as a <see cref="WriteSession"/> does, until it is disposed,
/// and the program must undo what it does: the driver script runs everything in a transaction it rolls back.
/// </summary>
public sealed class DriverSession : IDisposable
{
    private readonly IDisposable pass;

    internal DriverSession(string config, IDisposable pass)
    {
        Config = config;
        this.pass = pass;
    }

    /// <summary>
    /// The connection as the mssql driver's config, in JSON. It holds the password, so it is handed over in the environment
    /// only, never on a command line or in output.
    /// </summary>
    public string Config { get; }

    public void Dispose() => pass.Dispose();
}
