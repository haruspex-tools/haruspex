using Microsoft.Data.SqlClient;

namespace Haruspex.DifferentialTests.Server;

/// <summary>
/// A session in a database of one test's own (<see cref="ScratchDatabase.SessionAsync"/>), committing what it does, so that a
/// procedure can be run there as the shared database's are (<see cref="ProcedureRunner"/>).
/// </summary>
public sealed class ScratchSession : DatabaseSession
{
    internal ScratchSession(SqlConnection connection)
        : base(connection, null, null)
    {
    }
}
