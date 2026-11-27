using Haruspex.Collections;
using Haruspex.SqlServer.Model;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// One of SQL Server's own procedures, as the catalogue knows it (<see cref="SystemProcedures"/>): what a call returns and
/// what it sets. Each entry writes no table of the database and no temporary table of its caller.
/// </summary>
/// <param name="Name">Its name, in <c>sys</c>.</param>
/// <param name="Parameters">Its parameters, in order, as <c>sys.all_parameters</c> gives them.</param>
/// <param name="ResultsUnknownBecause">
/// Why its result sets are not known, when they aren't; null when a call returns none, as observed. No entry returns one.
/// </param>
/// <param name="ReturnCode">What its return code is, in words: its type and documented values.</param>
/// <param name="ObservedIn">The observation tests that pin the entry to the server.</param>
public sealed record SystemProcedure(
    ObjectName Name,
    EquatableArray<SystemParameter> Parameters,
    string? ResultsUnknownBecause,
    string ReturnCode,
    EquatableArray<string> ObservedIn)
{
    /// <summary>
    /// The argument a call passes each parameter it passes, by the parameter's name as <see cref="Parameters"/> lists it: those
    /// passed by position in that order (observed of <c>sp_invoke_external_rest_endpoint</c>:
    /// Arguments_by_position_are_in_the_order_sys_all_parameters_lists), then those passed by name, compared without case.
    /// </summary>
    public IReadOnlyDictionary<string, ExecuteParameter> Arguments(ExecuteSpecification execute)
    {
        ArgumentNullException.ThrowIfNull(execute);
        var arguments = new Dictionary<string, ExecuteParameter>(StringComparer.OrdinalIgnoreCase);
        var position = 0;
        foreach (var argument in execute.ExecutableEntity?.Parameters ?? [])
        {
            if (argument.Variable is { } named)
                arguments[named.Name] = argument;
            else if (position < Parameters.Count)
                arguments[Parameters[position++].Name] = argument;
        }

        return arguments;
    }
}
