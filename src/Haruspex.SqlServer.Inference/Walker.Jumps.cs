using Haruspex.Ir;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using static Haruspex.SqlServer.Inference.Typer;

namespace Haruspex.SqlServer.Inference;

// GOTO and its labels, and EXECUTE AS: the statements that change where control goes, or whose permissions the statements
// after them run with. README.md in this folder, "Flow statements", gives the rules.
internal sealed partial class Walker
{
    /// <summary>The body's labels and what each GOTO may be followed to, read before the walk.</summary>
    private BodyLabels labels = BodyLabels.None;

    /// <summary>
    /// For each label a GOTO jumps forward to, the states it jumps with, joined, until the walk reaches the label. A label is
    /// matched ignoring case, as the server matches it on a database of any collation (observed:
    /// A_goto_jumps_forward_or_back_out_of_blocks_and_into_them_but_not_into_try). It follows the server's collation, so on a
    /// server whose collation is case-sensitive two labels may differ only in case: a GOTO that names either is not followed
    /// (<see cref="BodyLabels.Unfollowed"/>).
    /// </summary>
    private readonly Dictionary<string, FlowState> jumps = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// <c>GOTO</c>: no path goes on to the next statement, and the state it jumps with joins the paths at its label. Only a jump
    /// forward to a label in the same statement list or one around it is followed, which the walk then reaches after the GOTO
    /// on every pass, a loop's included: out of a block, a loop, a TRY or a CATCH. A jump back makes a loop, and a jump into a
    /// block starts it partway, which the walk doesn't do; the server refuses a jump into TRY or CATCH (1026) when the
    /// procedure runs. Either makes the procedure's results unknown.
    /// </summary>
    private Outcome Jump(GoToStatement jump, FlowState state)
    {
        var at = At(jump);
        if (labels.Unfollowed(jump) is { } why)
        {
            blocker ??= new UnknownShape(IrUnknownReason.UnsupportedStatement, $"{why}, so the procedure's result sets cannot be listed.", at);
            return Outcome.Ends;
        }

        var name = jump.LabelName.Value;
        jumps[name] = FlowState.Join(state, jumps.GetValueOrDefault(name), at);
        return Outcome.Ends;
    }

    /// <summary>The state at a label: the one from the statement before it joined with those that jumped to it.</summary>
    private FlowState? Landed(LabelStatement label, FlowState? state) =>
        jumps.Remove(BodyLabels.Name(label), out var jumped) ? FlowState.Join(state, jumped, At(label)) : state;

    /// <summary>
    /// <c>EXECUTE AS</c>: the statements after it run with another user's permissions until <c>REVERT</c> or the procedure
    /// returns, and a name binds as before, in the module's schema (observed:
    /// <c>Execute_as_changes_permissions_and_masks_but_not_names_and_ends_when_the_procedure_returns</c>). Of what haruspex
    /// claims, only what a masked column gives rests on whose permissions they are: a module whose body impersonates anyone but
    /// its caller keeps every mask (<see cref="Model.Module.RunsAsAnother"/>). A query file's does not, so where the callers
    /// are declared to hold <c>UNMASK</c> one there makes its results unknown. <c>WITH COOKIE INTO</c>, which the server allows
    /// only outside a module, sets a variable, which is not modelled.
    /// </summary>
    private Outcome Impersonated(ExecuteAsStatement impersonate, FlowState state)
    {
        var at = At(impersonate);
        if (impersonate.Cookie is not null)
        {
            blocker ??= new UnknownShape(IrUnknownReason.UnsupportedStatement, $"The EXECUTE AS {Text(at)} sets a variable to a cookie, which is not modelled, so the result sets cannot be listed.", at);
            return Outcome.Ends;
        }

        if (impersonate.ExecuteContext.Kind != ExecuteAsOption.Caller && !summaries.MasksApply)
        {
            blocker ??= new UnknownShape(
                IrUnknownReason.UnsupportedStatement,
                $"The session declares that the callers hold UNMASK, but the EXECUTE AS {Text(at)} reads with another user's permissions, which may not, so the result sets cannot be listed.",
                at);
            return Outcome.Ends;
        }

        return Continue(state);
    }
}
