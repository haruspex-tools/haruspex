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
    /// can't be listed, and come back as one value), unnamed, no <c>OUTPUT</c> parameter, and no return code worth handing back
    /// (<c>docs/decisions/0060-a-call-returns-its-one-result-itself.md</c>). A named set is always returned under its name
    /// (<c>docs/decisions/0070-a-procedure-s-result-sets-are-named-all-or-none.md</c>).
    /// </summary>
    public bool ReturnsResultAlone => Results is null or [_] && !NamesSets && !HasOutput && !ReturnsCode;

    /// <summary>Whether the procedure's result sets are named, each returned under its name.</summary>
    public bool NamesSets => Procedure.SetNames is not null;

    /// <summary>Whether the function returns nothing: no result set, no <c>OUTPUT</c> parameter, no return code worth handing back.</summary>
    public bool ReturnsNothing => Results is [] && !HasOutput && !ReturnsCode;

    /// <summary>
    /// How many result sets every call returns: those listed before the first that may be missing. A set whose statement may
    /// not run, and every one after it, may be missing from what arrives (the IR's reader convention pads them); one before it
    /// can't be, so a call that returns fewer is a database out of step. A <c>FOR JSON</c> result absent only because no rows
    /// matched still arrives, as a set of one row of empty text (observed), so it counts. Any other reason to be absent, such as
    /// the config file's declaration, which doesn't say why, stops the count, which only checks less.
    /// </summary>
    public int SetsAlwaysReturned =>
        Procedure.Results is { } results ? results.TakeWhile(r => r.AbsentBecause.All(reason => reason.Rule == IrNullRule.NoRows)).Count() : 0;

    /// <summary>Whether the procedure has <c>OUTPUT</c> parameters, whose values come back.</summary>
    public bool HasOutput => Bindings.Any(b => b.IsOutput);
}
