using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Inference;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.Tests.Inference;

/// <summary>
/// The one environment of what filters and branches prove (<see cref="Facts"/>): extending it leaves the one before as it
/// was; a branch's proof is stronger than a filter's; rows, variables and a query's aggregates are looked up one way; and
/// sources are told apart by reference. The rules that fill it are tested in <c>RefinementTests</c> and <c>CaseTests</c>.
/// </summary>
public class FactsTests
{
    private static readonly BoundSource Lines = new("l", null, null, new SourceLocation("p.sql", 4, 1));

    [Fact]
    public void Extending_the_facts_leaves_the_ones_before_as_they_were()
    {
        var filter = new Facts.Found(Facts.Proof.WhereTheRowIsThere, null, [Lines]);
        filter.Value(Lines, "c:qty");
        filter.Row(Lines);

        var inside = Facts.None.With(filter);

        Assert.Equal(Facts.Proof.WhereTheRowIsThere, inside.Proved(Lines, "c:qty"));
        Assert.True(inside.Matched(Lines));
        Assert.Equal(Facts.Proof.None, Facts.None.Proved(Lines, "c:qty"));
        Assert.False(Facts.None.Matched(Lines));
    }

    [Fact]
    public void A_branch_proves_the_value_itself_which_outranks_a_filter()
    {
        var filter = new Facts.Found(Facts.Proof.WhereTheRowIsThere, null, [Lines]);
        filter.Value(Lines, "c:qty");
        var branch = new Facts.Found(Facts.Proof.OfTheValue, null, []);
        branch.Value(Lines, "c:qty");
        branch.Variable("@limit");

        var facts = Facts.None.With(filter).With(branch);

        Assert.Equal(Facts.Proof.OfTheValue, facts.Proved(Lines, "c:qty"));
        Assert.True(facts.Proved("@LIMIT"));
        Assert.False(Facts.None.With(filter).Proved("@limit"));
    }

    [Fact]
    public void An_aggregate_is_proved_only_of_its_own_query_and_sources_only_by_reference()
    {
        var query = new QuerySpecification();
        var found = new Facts.Found(Facts.Proof.WhereTheRowIsThere, query, [Lines]) { Rolled = new IrNullReason(IrNullRule.SubtotalRow, "p.sql:9:1") };
        found.Aggregate("SUM(l.qty)");
        var facts = Facts.None.With(found);

        Assert.True(facts.Proved(query, "SUM(l.qty)"));
        Assert.False(facts.Proved(new QuerySpecification(), "SUM(l.qty)"));
        Assert.Equal(IrNullRule.SubtotalRow, facts.Rolled(Lines, [])?.Rule);
        Assert.Null(facts.Rolled(Lines, [query]));

        // Equal as a record, but another source: one table read twice is two sources.
        Assert.Equal(Facts.Proof.None, Facts.None.With(Copy()).Proved(Lines with { }, "c:qty"));

        static Facts.Found Copy()
        {
            var copy = new Facts.Found(Facts.Proof.WhereTheRowIsThere, null, [Lines]);
            copy.Value(Lines, "c:qty");
            return copy;
        }
    }
}
