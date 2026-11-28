using System.Collections.Frozen;
using System.Collections.Immutable;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// Every ScriptDom node type the typer accepts as an expression (<c>Typer.Expression</c>, and the function table's
/// <c>TypingOf</c>), with how it reads each of the node's properties: what a rule reads to type it, a clause the function
/// table's rows read one by one, <c>COLLATE</c>, or one it ignores, with why; and so too the clauses that are nodes of their
/// own (<c>OVER</c>, <c>WITHIN GROUP</c>). <c>Typer.Expression</c> refuses any node type not listed before it reads it, so the
/// table is the gate, not a description of it.
/// </summary>
/// <remarks>
/// A property no rule reads is read as its default, so a node carrying it would be typed as another node: the claim would be
/// about other SQL (audit finding F005: <c>json_query(x WITH ARRAY WRAPPER)</c> was typed as <c>x</c>'s object). So no property
/// goes unlisted. <c>ClauseTableTests</c> compares this table with ScriptDom's own properties, by reflection, in both
/// directions, and checks that ScriptDom derives no node type from one listed here, so a new version of ScriptDom that adds a
/// property or a node fails a test until someone decides how it is read
/// (docs/decisions/0113-a-clause-no-rule-reads-makes-the-call-unknown.md).
/// </remarks>
internal static class AcceptedNodes
{
    /// <summary>How the typer reads a property.</summary>
    internal enum Use
    {
        /// <summary>The rule for the node reads it, whatever it holds.</summary>
        Read,

        /// <summary>A clause (<see cref="CallClauses"/>): a call carrying it is unknown unless its row reads it.</summary>
        Clause,

        /// <summary><c>COLLATE</c>, read for every expression by <c>Typer.Collated</c>.</summary>
        Collation,

        /// <summary>Not read, because it can't change what the node gives; the reason says why.</summary>
        Ignored,
    }

    /// <summary>A property of a node type, how it is read, and why.</summary>
    internal sealed record Property(string Name, Use Use, string Why);

    private static Property Read(string name, string why) => new(name, Use.Read, why);

    private static readonly Property Collate = new(nameof(PrimaryExpression.Collation), Use.Collation, "COLLATE: a Unicode string keeps its value, any other string only its type, and anything else is not modelled.");

    private static readonly Property LiteralValue = Read(nameof(Literal.Value), "The literal's text, which its rule reads.");

    private static readonly Property LiteralType = new(nameof(Literal.LiteralType), Use.Ignored, "Says what the node's own type says, by which the typer chooses the literal's rule.");

    private static readonly Property LargeObject = new(nameof(StringLiteral.IsLargeObject), Use.Ignored, "Says the literal is longer than its type's limit, which its rule reads from its text.");

    private static readonly Property Parameters = Read(nameof(FunctionCall.Parameters), "The arguments: their number chooses the row's form, and the rule reads each.");

    /// <summary>The clauses <see cref="CallClauses"/> holds in a property of a node of this type.</summary>
    private static IEnumerable<Property> Clauses(Type node) =>
        CallClauses.All.Where(c => c.Node == node.Name).Select(c => new Property(c.Property, Use.Clause, $"{c.Written}: read only by the rows that say so."));

    /// <summary>
    /// The clauses of a call that are nodes with properties of their own, which a row reading the clause reads, each
    /// property listed as an expression's are.
    /// </summary>
    internal static FrozenDictionary<Type, ImmutableArray<Property>> ClauseProperties { get; } = new Dictionary<Type, ImmutableArray<Property>>
    {
        [typeof(OverClause)] =
        [
            Read(nameof(OverClause.WindowName), "A window the query's WINDOW clause defines, which NamedWindows follows."),
            Read(nameof(OverClause.Partitions), "The partition, which NamedWindows resolves with the name's."),
            Read(nameof(OverClause.OrderByClause), "The order, which NamedWindows resolves with the name's."),
            Read(nameof(OverClause.WindowFrameClause), "The frame, which may leave a frame empty (Typer.MayBeEmpty)."),
        ],
        [typeof(WithinGroupClause)] =
        [
            Read(nameof(WithinGroupClause.OrderByClause), "The order: a percentile's value, and STRING_AGG's items' order."),
            .. Clauses(typeof(WithinGroupClause)),
        ],
    }.ToFrozenDictionary();

    /// <summary>Each node type accepted, and every property it has beyond <see cref="TSqlFragment"/>'s own.</summary>
    internal static FrozenDictionary<Type, ImmutableArray<Property>> Properties { get; } = new Dictionary<Type, ImmutableArray<Property>>
    {
        [typeof(ColumnReferenceExpression)] =
        [
            Read(nameof(ColumnReferenceExpression.ColumnType), "A MERGE's $action, or a column the binder resolves."),
            Read(nameof(ColumnReferenceExpression.MultiPartIdentifier), "The name the binder resolves."),
            Collate,
        ],
        [typeof(ParenthesisExpression)] = [Read(nameof(ParenthesisExpression.Expression), "Typed as itself."), Collate],
        [typeof(ScalarSubquery)] = [Read(nameof(ScalarSubquery.QueryExpression), "The query whose one column it gives."), Collate],
        [typeof(VariableReference)] = [Read(nameof(VariableReference.Name), "The variable whose value it gives."), Collate],
        [typeof(IntegerLiteral)] = [LiteralType, LiteralValue, Collate],
        [typeof(StringLiteral)] = [LiteralType, Read(nameof(StringLiteral.IsNational), "N'' makes it nvarchar."), LargeObject, LiteralValue, Collate],
        [typeof(NumericLiteral)] = [LiteralType, LiteralValue, Collate],
        [typeof(BinaryLiteral)] = [LiteralType, LargeObject, LiteralValue, Collate],
        [typeof(RealLiteral)] = [LiteralType, LiteralValue, Collate],
        [typeof(MoneyLiteral)] = [LiteralType, LiteralValue, Collate],
        [typeof(NullLiteral)] = [LiteralType, LiteralValue, Collate],
        [typeof(SearchedCaseExpression)] =
        [
            Read(nameof(SearchedCaseExpression.WhenClauses), "Each condition and result."),
            Read(nameof(CaseExpression.ElseExpression), "The result when no condition holds, NULL when there is none."),
            Collate,
        ],
        [typeof(SimpleCaseExpression)] =
        [
            Read(nameof(SimpleCaseExpression.InputExpression), "The value compared."),
            Read(nameof(SimpleCaseExpression.WhenClauses), "Each value compared with and its result."),
            Read(nameof(CaseExpression.ElseExpression), "The result when no value matches, NULL when there is none."),
            Collate,
        ],
        [typeof(BinaryExpression)] =
        [
            Read(nameof(BinaryExpression.BinaryExpressionType), "The operator."),
            Read(nameof(BinaryExpression.FirstExpression), "The left operand."),
            Read(nameof(BinaryExpression.SecondExpression), "The right operand."),
        ],
        [typeof(UnaryExpression)] =
        [
            Read(nameof(UnaryExpression.UnaryExpressionType), "The operator."),
            Read(nameof(UnaryExpression.Expression), "The operand."),
        ],
        [typeof(FunctionCall)] =
        [
            Read(nameof(FunctionCall.CallTarget), "A schema names a user's function, and an xml value or column an XML method's receiver; a built-in has none."),
            Read(nameof(FunctionCall.FunctionName), "The function, by which its row is found."),
            Parameters,
            .. Clauses(typeof(FunctionCall)),
            Collate,
        ],
        [typeof(LeftFunctionCall)] = [Parameters, Collate],
        [typeof(RightFunctionCall)] = [Parameters, Collate],
        [typeof(ParameterlessCall)] = [Read(nameof(ParameterlessCall.ParameterlessCallType), "Which function: only CURRENT_TIMESTAMP has a row."), Collate],
        [typeof(GlobalVariableExpression)] = [Read(nameof(GlobalVariableExpression.Name), "Which value, by which its row is found: only those in the catalogue have one."), Collate],
        [typeof(AtTimeZoneCall)] = [Read(nameof(AtTimeZoneCall.DateValue), "The date."), Read(nameof(AtTimeZoneCall.TimeZone), "The time zone."), Collate],
        [typeof(CastCall)] = [Read(nameof(CastCall.DataType), "The target type."), Read(nameof(CastCall.Parameter), "The value converted."), Collate],
        [typeof(ConvertCall)] =
        [
            Read(nameof(ConvertCall.DataType), "The target type."),
            Read(nameof(ConvertCall.Parameter), "The value converted."),
            Read(nameof(ConvertCall.Style), "The style, NULL for a null one."),
            Collate,
        ],
        [typeof(TryCastCall)] = [Read(nameof(TryCastCall.DataType), "The target type."), Read(nameof(TryCastCall.Parameter), "The value converted."), Collate],
        [typeof(TryConvertCall)] =
        [
            Read(nameof(TryConvertCall.DataType), "The target type."),
            Read(nameof(TryConvertCall.Parameter), "The value converted."),
            Read(nameof(TryConvertCall.Style), "The style, NULL for a null one."),
            Collate,
        ],
        [typeof(CoalesceExpression)] = [Read(nameof(CoalesceExpression.Expressions), "Each value, in order."), Collate],
        [typeof(NullIfExpression)] = [Read(nameof(NullIfExpression.FirstExpression), "The value."), Read(nameof(NullIfExpression.SecondExpression), "The value that makes it NULL."), Collate],
        [typeof(IIfCall)] =
        [
            Read(nameof(IIfCall.Predicate), "The condition."),
            Read(nameof(IIfCall.ThenExpression), "The result when it holds."),
            Read(nameof(IIfCall.ElseExpression), "The result when it doesn't."),
            Collate,
        ],
    }.ToFrozenDictionary();
}
