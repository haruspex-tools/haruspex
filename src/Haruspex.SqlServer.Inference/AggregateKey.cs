using Haruspex.SqlServer.Syntax;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// An aggregate call of one query, as what HAVING or a branch proves of it is kept (<see cref="Facts"/>): by its text as
/// written, on one line (<see cref="FragmentText.OneLine"/>), compared exactly.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the text is a sound identity.</b> A proof is kept per query (<see cref="Facts.Proved(QuerySpecification?, AggregateKey)"/>),
/// and within one query every reference is bound in the same scope, so two calls written alike name the same function over the
/// same columns and compute the same value in each group: what holds of one holds of the other. Two calls written differently
/// may still be one aggregate (<c>MAX(t.name)</c> and <c>max(name)</c>), and are kept apart, so a proof of one is not used for
/// the other: a fact is lost, which makes a claim narrower, and never a wrong one. The tests pin both
/// (<c>Having_proves_an_aggregate_written_alike_and_no_other</c>).
/// </para>
/// <para>
/// Keying by the bound aggregate (the function and each argument's resolution) would match those too, and claim more; that
/// is a change in what is inferred, to be proved against the server before it is made.
/// </para>
/// </remarks>
/// <param name="Text">The call as written, on one line.</param>
internal sealed record AggregateKey(string Text)
{
    /// <summary>The key of an aggregate call.</summary>
    public static AggregateKey Of(FunctionCall call) => new(FragmentText.OneLine(call));
}
