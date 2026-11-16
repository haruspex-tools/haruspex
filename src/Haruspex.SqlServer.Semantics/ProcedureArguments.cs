using Haruspex.SqlServer.Model;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// Which parameter of a procedure each argument of a call of it is passed to: by name (<c>@why = @w</c>), the name compared as
/// SQL Server compares a variable's, or else by place. An argument that names no parameter, or is past the last, is left out.
/// </summary>
public static class ProcedureArguments
{
    /// <summary>Each argument the call passes, with the parameter of <paramref name="callee"/> it is passed to, in the order written.</summary>
    /// <param name="call">The call's procedure and arguments.</param>
    /// <param name="callee">The procedure it calls, as binding found it.</param>
    public static IReadOnlyList<(ScalarExpression Value, Parameter Parameter)> Matched(ExecutableProcedureReference call, Procedure callee)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(callee);
        var matched = new List<(ScalarExpression, Parameter)>();
        for (var i = 0; i < call.Parameters.Count; i++)
        {
            var argument = call.Parameters[i];
            var target = argument.Variable is { } named
                ? callee.Parameters.FirstOrDefault(p => new LocalName(p.Name) == new LocalName(named.Name))
                : i < callee.Parameters.Count ? callee.Parameters[i] : null;
            if (target is not null && argument.ParameterValue is { } value)
                matched.Add((value, target));
        }

        return matched;
    }
}
