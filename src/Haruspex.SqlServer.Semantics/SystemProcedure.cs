using Haruspex.Collections;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// One of SQL Server's own procedures, as the catalogue knows it (<see cref="SystemProcedures"/>): what a call returns and
/// what it sets. Each entry writes no table of the database and no temporary table of its caller.
/// </summary>
/// <param name="Name">Its name, in <c>sys</c>.</param>
/// <param name="Parameters">Its parameters, in order, as <c>sys.all_parameters</c> gives them.</param>
/// <param name="ResultsUnknownBecause">
/// Why its result sets are not known, when they aren't; null when a call returns none, as observed. No entry returns one yet.
/// </param>
/// <param name="ReturnCode">What its return code is, in words: its type and documented values.</param>
/// <param name="ObservedIn">The observation tests that pin the entry to the server.</param>
public sealed record SystemProcedure(
    ObjectName Name,
    EquatableArray<SystemParameter> Parameters,
    string? ResultsUnknownBecause,
    string ReturnCode,
    EquatableArray<string> ObservedIn);
