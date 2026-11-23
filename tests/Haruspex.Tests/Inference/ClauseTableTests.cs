using System.Reflection;
using Haruspex.SqlServer.Inference;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.Tests.Inference;

/// <summary>
/// The typer's table of the ScriptDom nodes it accepts (<see cref="AcceptedNodes"/>), held to ScriptDom by reflection: every
/// property of every node listed is read, a clause, <c>COLLATE</c> or ignored with a reason, and no listed property is gone; no
/// node type derives from one listed. A new ScriptDom that adds a property or a node fails here until someone decides how it is
/// read (decision 0113).
/// </summary>
public class ClauseTableTests
{
    private static readonly Assembly ScriptDom = typeof(TSqlFragment).Assembly;

    /// <summary>
    /// What the table gets wrong about a node type: a property it doesn't list, one it lists that the node lacks or lists twice,
    /// and one with no reason.
    /// </summary>
    private static List<string> Problems(Type node, IReadOnlyCollection<AcceptedNodes.Property> listed)
    {
        var properties = node.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.DeclaringType != typeof(TSqlFragment))
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);
        var names = listed.Select(p => p.Name).ToList();
        return
        [
            .. properties.Except(names).Order(StringComparer.Ordinal).Select(p => $"{node.Name}.{p}: not listed"),
            .. names.Except(properties).Order(StringComparer.Ordinal).Select(p => $"{node.Name}.{p}: listed, but the node has no such property"),
            .. names.GroupBy(n => n, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => $"{node.Name}.{g.Key}: listed twice"),
            .. listed.Where(p => string.IsNullOrWhiteSpace(p.Why)).Select(p => $"{node.Name}.{p.Name}: no reason"),
        ];
    }

    [Fact]
    public void Every_property_of_every_node_the_typer_accepts_is_listed_with_how_it_is_read()
    {
        var problems = AcceptedNodes.Properties.SelectMany(n => Problems(n.Key, n.Value)).ToList();
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void No_parsed_node_type_derives_from_one_the_typer_accepts()
    {
        // A derived node would be typed as its base, its own properties unread.
        var derived = ScriptDom.GetTypes()
            .Where(t => t.BaseType is { } parent && AcceptedNodes.Properties.ContainsKey(parent))
            .Select(t => $"{t.Name} derives from {t.BaseType!.Name}")
            .ToList();
        Assert.Empty(derived);
    }

    [Fact]
    public void Each_clause_of_a_function_call_is_one_property_and_listed_as_a_clause()
    {
        Assert.Equal(Enum.GetValues<Clause>().Where(c => c != Clause.None), CallClauses.All.Select(c => c.Clause));
        Assert.Equal(
            CallClauses.All.Select(c => c.Property),
            AcceptedNodes.Properties[typeof(FunctionCall)].Where(p => p.Use == AcceptedNodes.Use.Clause).Select(p => p.Name));
    }

    [Fact]
    public void Every_clause_is_seen_on_a_call_that_carries_it_and_on_no_other()
    {
        var calls = new Dictionary<Clause, string>
        {
            [Clause.Over] = "SELECT ROW_NUMBER() OVER (ORDER BY a) FROM t;",
            [Clause.Distinct] = "SELECT COUNT(DISTINCT a) FROM t;",
            [Clause.WithinGroup] = "SELECT STRING_AGG(a, ',') WITHIN GROUP (ORDER BY a) FROM t;",
            [Clause.NullTreatment] = "SELECT FIRST_VALUE(a) IGNORE NULLS OVER (ORDER BY a) FROM t;",
            [Clause.TrimSide] = "SELECT TRIM(LEADING 'x' FROM a) FROM t;",
            [Clause.KeyValuePairs] = "SELECT JSON_OBJECTAGG(a: b) FROM t;",
            [Clause.JsonOrderBy] = "SELECT JSON_ARRAYAGG(a ORDER BY b) FROM t;",
            [Clause.NullOnNull] = "SELECT JSON_ARRAYAGG(a NULL ON NULL) FROM t;",
            [Clause.Returning] = "SELECT JSON_VALUE(a, '$.b' RETURNING int) FROM t;",
            [Clause.ArrayWrapper] = "SELECT JSON_QUERY(a WITH ARRAY WRAPPER) FROM t;",
        };
        Assert.Equal(CallClauses.All.Select(c => c.Clause), calls.Keys);
        foreach (var (clause, sql) in calls)
            Assert.Equal(clause | (sql.Contains(" OVER ", StringComparison.Ordinal) ? Clause.Over : Clause.None), CallClauses.Of(OnlyCall(sql)));
        Assert.Equal(Clause.None, CallClauses.Of(OnlyCall("SELECT COUNT(ALL a) FROM t;")));
        Assert.Equal(Clause.None, CallClauses.Of(OnlyCall("SELECT UPPER(a) FROM t;")));
    }

    [Fact]
    public void A_property_the_table_does_not_list_is_found_and_so_is_one_it_lists_that_the_node_lacks()
    {
        // The check itself, over a node a later ScriptDom might add a property to.
        var listed = AcceptedNodes.Properties[typeof(FunctionCall)];
        Assert.Equal(["FakeCall.Sampled: not listed"], Problems(new FakeCall { Sampled = true }.GetType(), listed));
        Assert.Equal(
            ["FunctionCall.Sampled: listed, but the node has no such property"],
            Problems(typeof(FunctionCall), [.. listed, new AcceptedNodes.Property("Sampled", AcceptedNodes.Use.Clause, "A clause no version has.")]));
    }

    /// <summary>A function call with a clause ScriptDom doesn't have.</summary>
    private sealed class FakeCall : FunctionCall
    {
        public bool Sampled { get; set; }
    }

    private static FunctionCall OnlyCall(string sql)
    {
        var parsed = new TSql170Parser(initialQuotedIdentifiers: true).Parse(new StringReader(sql), out var errors);
        Assert.Empty(errors);
        var finder = new CallFinder();
        parsed.Accept(finder);
        return Assert.Single(finder.Calls);
    }

    private sealed class CallFinder : TSqlFragmentVisitor
    {
        public List<FunctionCall> Calls { get; } = [];

        public override void ExplicitVisit(FunctionCall node)
        {
            Calls.Add(node);
            base.ExplicitVisit(node);
        }
    }
}
