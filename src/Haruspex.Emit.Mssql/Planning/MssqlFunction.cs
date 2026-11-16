using Haruspex.Collections;
using Haruspex.Ir;

namespace Haruspex.Emit.Mssql.Planning;

/// <summary>One procedure's or query file's function, decided before anything is written: what it is named, sends and reads.</summary>
/// <param name="Name">The function's name: the object's settled name with a lower-case first word (<c>getOrder</c>).</param>
/// <param name="Procedure">The procedure or query file.</param>
/// <param name="Bindings">Its parameters, in declared order.</param>
/// <param name="Results">How each result set is read, in order; null when its results can't be listed, and the function hands over every result set as it arrives.</param>
internal sealed record MssqlFunction(string Name, IrObject Procedure, EquatableArray<Binding> Bindings, EquatableArray<IrEncoding>? Results)
{
    /// <summary>
    /// For a query file, what <c>sp_executesql</c> is sent: the text to run, and the declaration of its parameters
    /// (<c>@order_id int, @note nvarchar(40)</c>), empty when it has none; null for a procedure, which is executed by its name.
    /// </summary>
    public (string Text, string Declaration)? Batch { get; init; }

    /// <summary>Whether a call may end with a return code other than 0, which then comes back as <c>returnValue</c>.</summary>
    public bool ReturnsCode => Procedure.ReturnsCode;

    /// <summary>
    /// Whether the function returns its one result set itself, rather than an object naming it: when there is one (or the sets
    /// can't be listed, and come back as one value), no <c>OUTPUT</c> parameter, and no return code worth handing back
    /// (<c>docs/decisions/0060-a-call-returns-its-one-result-itself.md</c>).
    /// </summary>
    public bool ReturnsResultAlone => Results is null or [_] && !HasOutput && !ReturnsCode;

    /// <summary>Whether the function returns nothing: no result set, no <c>OUTPUT</c> parameter, no return code worth handing back.</summary>
    public bool ReturnsNothing => Results is [] && !HasOutput && !ReturnsCode;

    /// <summary>Whether the procedure has <c>OUTPUT</c> parameters, whose values come back.</summary>
    public bool HasOutput => Bindings.Any(b => b.IsOutput);
}
