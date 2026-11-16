using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using static Haruspex.SqlServer.Inference.Typer;

namespace Haruspex.SqlServer.Inference;

// What a guard proves, as the walk carries it: an IF's arms narrowed by their condition, what a SELECT that may assign nothing
// would have assigned, and what may end a row an existence guard found. README.md in this folder, "Guards", gives the rules,
// and decision 0053 why the facts are kept in the state this way; the conditions themselves are read in Typer.Guards.cs.
internal sealed partial class Walker
{
    /// <summary>
    /// The state on one arm of an <c>IF</c>: each variable its condition proves non-null there (<see cref="Typer.Proves"/>)
    /// narrowed to its value without NULL. So after <c>IF @x IS NULL RETURN</c>, whose arm ends, the rest of the body has
    /// <c>@x</c> non-null. The fact lives in the value, so it lasts exactly as long as the value does: an assignment replaces it,
    /// and where paths meet (the end of the IF, a CATCH, a loop's head) it is joined with what the other paths hold. The guide's
    /// chapter 6, "What a guard proves", explains it.
    /// </summary>
    private FlowState Narrowed(FlowState state, BooleanExpression condition, bool held)
    {
        var proved = typer.Proves(condition, held, Reading(state));
        var narrowed = state;

        // A SELECT proved to have read a row holds what it assigned (PossibleAssignment): by @@ROWCOUNT tested straight after
        // it, or by a witness, NULL until the SELECT, proved not NULL now.
        var rows = ProvesRows(condition, held);
        foreach (var possible in state.Possible.Where(p => (p.Counted && rows) || p.Witnesses.Any(proved.Proved)))
        {
            foreach (var (name, value) in possible.Values)
                narrowed = narrowed.WithVariable(name, value);
        }

        // A row an EXISTS on this arm proves was there (FoundRow).
        if (typer.Finds(condition, held, Reading(state)) is { } found)
            narrowed = narrowed.WithFound(found);

        foreach (var (name, value) in narrowed.Variables)
        {
            if (value.Shape is not UnknownShape && value.NullableBecause.Count > 0 && proved.Proved(name))
                narrowed = narrowed.WithVariable(name, value.WithNullableBecause([]));
        }

        return narrowed;
    }

    /// <summary>
    /// Whether a statement other than a write <c>Modify</c> reads may change a permanent table's rows: a call, which may write
    /// anything, or <c>TRUNCATE</c> of a permanent table. A write's own target is read where it is bound (<c>Modify</c>).
    /// </summary>
    private static bool MayWrite(TSqlStatement statement) => statement switch
    {
        ExecuteStatement => true,
        InsertStatement { InsertSpecification.InsertSource: ExecuteInsertSource } => true,
        TruncateTableStatement truncate => Binder.Temporary(truncate.TableName) is null,
        _ => false,
    };

    /// <summary>
    /// The state after a <c>SELECT @a = …, @b = …</c> that may read no row, with what it assigns when it reads one kept for a
    /// guard that proves it did (<see cref="PossibleAssignment"/>). Its witnesses are the variables it assigns that were NULL
    /// on every path before it.
    /// </summary>
    /// <param name="before">The state before the SELECT.</param>
    /// <param name="after">The state after it, each variable joined with what it held.</param>
    /// <param name="read">Each variable it assigns, with what it holds if a row is read.</param>
    private static FlowState Remembered(FlowState before, FlowState after, List<KeyValuePair<LocalName, Value>> read) =>
        read.Count == 0
            ? after
            : after.WithPossible(new PossibleAssignment([.. read], [.. read.Select(r => r.Key).Distinct().Where(before.Unassigned.Contains)], Counted: true));
}
