using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// The catalogue of SQL Server's own procedures whose calls haruspex follows: what each returns, its return code and its
/// parameters, as data. A call of any other <c>sp_</c> or <c>xp_</c> name stays unknown (<see cref="UnknowableCallKind.SystemProcedure"/>),
/// and <c>sp_executesql</c> is dynamic SQL. Each entry is observed on SQL Server 2025 (ExecObservationTests,
/// <c>Each_system_procedure_of_the_catalogue_has_the_parameters_it_lists</c> and the test the entry names); one the test server
/// can't run is typed from Microsoft's documentation and leaves unknown what it doesn't observe. Every entry's return code is an
/// <c>int</c>, never NULL once the call returns, which is true of every procedure's.
/// </summary>
public static class SystemProcedures
{
    /// <summary>The entries.</summary>
    public static IReadOnlyList<SystemProcedure> All { get; } =
    [
        // Takes an application lock and returns no result set (observed): 0 or 1 when granted, -1 timed out, -2 cancelled,
        // -3 chosen as a deadlock victim, -999 an argument refused (documented; 0 and -999 observed).
        new(
            new ObjectName("sys", "sp_getapplock"),
            [
                new("@Resource", "nvarchar(255)"), new("@LockMode", "varchar(32)"), new("@LockOwner", "varchar(32)"),
                new("@LockTimeout", "int"), new("@DbPrincipal", "sysname"),
            ],
            ResultsUnknownBecause: null,
            "int: 0 or 1 granted, -1 timed out, -2 cancelled, -3 a deadlock victim, -999 an argument refused",
            ["Sp_getapplock_and_sp_releaseapplock_return_no_result_set_and_an_int"]),

        // Releases one; returns no result set (observed): 0, or -999 when the lock isn't held (with message 1223) or an
        // argument is refused.
        new(
            new ObjectName("sys", "sp_releaseapplock"),
            [new("@Resource", "nvarchar(255)"), new("@LockOwner", "varchar(32)"), new("@DbPrincipal", "sysname")],
            ResultsUnknownBecause: null,
            "int: 0 released, -999 not held or an argument refused",
            ["Sp_getapplock_and_sp_releaseapplock_return_no_result_set_and_an_int"]),

        // Parses a document for OPENXML and sets the handle passed OUTPUT; returns no result set and 0 (observed). XML that
        // isn't well formed is an error (6602) that ends the batch. An extended procedure: sys.all_parameters lists none, so
        // none is listed here; Microsoft documents three: the handle, an int OUTPUT, the text and its namespaces. Nothing it does changes
        // what a query returns but OPENXML's rows, which the document's data decides.
        new(
            new ObjectName("sys", "sp_xml_preparedocument"),
            [],
            ResultsUnknownBecause: null,
            "int: 0 when the document is parsed",
            ["Sp_xml_preparedocument_and_sp_xml_removedocument_return_no_result_set"]),

        // Frees a handle; returns no result set (observed): 0, or 1 with error 8179 when no document has the handle. An
        // extended procedure, listing no parameters as the one above.
        new(
            new ObjectName("sys", "sp_xml_removedocument"),
            [],
            ResultsUnknownBecause: null,
            "int: 0 freed, 1 when no document has the handle",
            ["Sp_xml_preparedocument_and_sp_xml_removedocument_return_no_result_set"]),

        // SQL Server 2025. Disabled on the test server ('external rest endpoint enabled' is 0, error 31643), so what a call
        // returns is not observed. Microsoft's page documents no result set, the response in @response OUTPUT as JSON (or
        // XML) and a return code of 0 or the HTTP status; until a server observes it, its result sets stay unknown.
        new(
            new ObjectName("sys", "sp_invoke_external_rest_endpoint"),
            [
                new("@url", "nvarchar(4000)"), new("@payload", "nvarchar(max)"), new("@headers", "nvarchar(4000)"), new("@method", "nvarchar(6)"),
                new("@timeout", "smallint"), new("@credential", "sysname"), new("@response", "nvarchar(max)", IsOutput: true), new("@retry_count", "tinyint"),
            ],
            ResultsUnknownBecause: "it is disabled on the server haruspex is tested against, so what it returns is not observed; its documentation lists no result set",
            "int: 0 for an HTTP 2xx status, else the status received",
            ["Sp_invoke_external_rest_endpoint_is_disabled_on_the_test_server"]),
    ];

    /// <summary>The entry for a procedure named <paramref name="name"/> (its last part), or null.</summary>
    public static SystemProcedure? Find(string name) =>
        All.FirstOrDefault(p => p.Name.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}
