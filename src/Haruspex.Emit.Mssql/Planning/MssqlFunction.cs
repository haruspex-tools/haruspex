using Haruspex.Collections;
using Haruspex.Ir;

namespace Haruspex.Emit.Mssql.Planning;

/// <summary>One procedure's function, decided before anything is written: what it is named, sends and reads.</summary>
/// <param name="Name">The function's name: the procedure's settled name with a lower-case first word (<c>getOrder</c>).</param>
/// <param name="Procedure">The procedure.</param>
/// <param name="Bindings">Its parameters, in declared order.</param>
/// <param name="Results">How each result set is read, in order; null when its results can't be listed, and the function hands over every result set as it arrives.</param>
internal sealed record MssqlFunction(string Name, IrObject Procedure, EquatableArray<Binding> Bindings, EquatableArray<IrEncoding>? Results)
{
    /// <summary>Whether the procedure has <c>OUTPUT</c> parameters, whose values come back.</summary>
    public bool HasOutput => Bindings.Any(b => b.IsOutput);
}
