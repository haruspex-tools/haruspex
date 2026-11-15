using System.Reflection;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.Tests.Coverage;

/// <summary>
/// The constructs the coverage page must list, read from ScriptDom itself so that none is missed by forgetting it: every
/// concrete node type of the kinds a procedure's body is made of, and every member of the enums that say which operator, join
/// or set operator a node is. And the constructs a piece of SQL holds, to check that an example shows what its entry claims.
/// </summary>
internal static class ScriptDomNodes
{
    /// <summary>
    /// The kinds of node a body is made of, each with every concrete type ScriptDom derives from it: what a statement is, and
    /// the parts that decide what it gives (a call's target and options, an UPDATE's SET, a method's target). Left out: the
    /// options of definitions (a table's, an index's, a login's), which only DDL holds, and data types, which the catalogue of
    /// built-in types covers.
    /// </summary>
    private static readonly Type[] Kinds =
    [
        typeof(TSqlStatement), typeof(ScalarExpression), typeof(BooleanExpression), typeof(TableReference), typeof(QueryExpression),
        typeof(SelectElement), typeof(ForClause), typeof(GroupingSpecification), typeof(InsertSource), typeof(MergeAction),
        typeof(TableHint), typeof(OptimizerHint), typeof(ExecutableEntity), typeof(ExecuteOption), typeof(ResultSetDefinition),
        typeof(SetClause), typeof(CallTarget),
    ];

    /// <summary>The clauses of a query or a write, which derive from nothing more particular than a fragment.</summary>
    private static readonly Type[] Clauses =
    [
        typeof(WithCtesAndXmlNamespaces), typeof(CommonTableExpression), typeof(XmlNamespaces), typeof(FromClause), typeof(WhereClause),
        typeof(GroupByClause), typeof(HavingClause), typeof(OrderByClause), typeof(TopRowFilter), typeof(OffsetClause), typeof(OverClause),
        typeof(WindowFrameClause), typeof(WindowClause), typeof(WindowDefinition), typeof(WithinGroupClause), typeof(TableSampleClause),
        typeof(OutputClause), typeof(OutputIntoClause), typeof(TemporalClause), typeof(MergeActionClause), typeof(FetchType),
        typeof(CursorDefinition), typeof(WindowDelimiter), typeof(ExpressionWithSortOrder),
    ];

    /// <summary>The enums whose members are operators, joins, set operators and assignments, each an entry of its own.</summary>
    private static readonly Type[] Operators =
    [
        typeof(BinaryExpressionType), typeof(UnaryExpressionType), typeof(AssignmentKind), typeof(BooleanComparisonType),
        typeof(BooleanBinaryExpressionType), typeof(BooleanTernaryExpressionType), typeof(SubqueryComparisonPredicateType),
        typeof(QualifiedJoinType), typeof(UnqualifiedJoinType), typeof(BinaryQueryExpressionType),
    ];

    /// <summary>Every construct the page must list: a node type by its name, an enum member as <c>Type.Member</c>.</summary>
    public static IReadOnlySet<string> All { get; } = new SortedSet<string>(
        typeof(TSqlFragment).Assembly.GetExportedTypes()
            .Where(t => !t.IsAbstract && Kinds.Any(k => k.IsAssignableFrom(t)))
            .Concat(Clauses)
            .Select(t => t.Name)
            .Concat(Operators.SelectMany(e => Enum.GetNames(e).Select(m => $"{e.Name}.{m}"))),
        StringComparer.Ordinal);

    /// <summary>
    /// The constructs a procedure body holds, parsed as SQL Server 2025's parser does: each node's type and each operator
    /// member a node names; or null, with the parser's errors, when it doesn't parse.
    /// </summary>
    public static (IReadOnlySet<string>? Constructs, string Errors) In(string body)
    {
        var parsed = new TSql170Parser(initialQuotedIdentifiers: true).Parse(new StringReader($"CREATE PROCEDURE dbo.p AS\n{body}"), out var errors);
        if (errors.Count > 0)
            return (null, string.Join("; ", errors.Select(e => e.Message)));
        var found = new Collector();
        parsed.Accept(found);
        return (found.Constructs, "");
    }

    /// <summary>Visits every node, recording its type and the operator members its properties name.</summary>
    private sealed class Collector : TSqlFragmentVisitor
    {
        public HashSet<string> Constructs { get; } = new(StringComparer.Ordinal);

        public override void Visit(TSqlFragment node)
        {
            Constructs.Add(node.GetType().Name);
            foreach (var property in node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => Operators.Contains(p.PropertyType)))
                Constructs.Add($"{property.PropertyType.Name}.{property.GetValue(node)}");
        }
    }
}
