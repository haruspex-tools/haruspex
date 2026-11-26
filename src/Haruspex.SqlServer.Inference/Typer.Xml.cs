using System.Text.RegularExpressions;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// The methods of an <c>xml</c> value: <c>x.value('path', 'type')</c> typed by its type argument and always nullable,
/// <c>x.exist('path')</c> a <c>bit</c> and <c>x.query('path')</c> untyped <c>xml</c>, each NULL exactly when <c>x</c> is, and
/// <c>x.nodes('path')</c> in FROM, a row for each node its path finds. Every rule was observed on SQL Server 2025 CU10
/// (<c>XmlObservationTests</c>, <c>XmlExpressionObservationTests</c>). Why:
/// docs/decisions/0133-an-xml-value-is-a-kind-of-its-own-a-branded-string.md and
/// docs/decisions/0137-xml-s-nodes-query-for-xml-and-openxml-are-typed-as-the-server-gives-them.md.
/// </summary>
internal sealed partial class Typer
{
    /// <summary>
    /// A call of an <c>xml</c> value's method, or null when <paramref name="call"/> is not one: a call whose target is an
    /// expression (<c>@x.value(…)</c>, <c>CAST(… AS xml).exist(…)</c>), or a column of the query (<c>p.spec.value(…)</c>, and
    /// <c>spec.value(…)</c>, which the server refuses as ambiguous, error 327, when a function <c>spec.value</c> exists too).
    /// A one-part target that is no column in scope is a function's schema, for <see cref="CallFunction"/>.
    /// </summary>
    private Value? XmlMethodCall(FunctionCall call, ReadScope scope)
    {
        // Only a name XML's methods have; any other (a CLR value's ToString()) is left as it was, not modelled.
        if (XmlMethods.Named(call.FunctionName.Value) is not { } method)
            return null;

        var target = call.CallTarget switch
        {
            ExpressionCallTarget expression => expression.Expression,
            MultiPartIdentifierCallTarget { MultiPartIdentifier: var parts } when AsColumn(parts) is { } column
                && (parts.Count > 1 || binder.Resolve(column, scope.Bound) is TableColumn or RelationColumn or UnlistedColumn) => column,
            _ => null,
        };
        if (target is null)
            return null;

        // The parser gives a method call no clause; one that carried any would be other SQL.
        if (CallClauses.Of(call) != Clause.None)
            return NotModelled(call, "an XML method call with a clause is not modelled.");

        var receiver = XmlReceiver(target, scope);
        if (receiver.Shape is UnknownShape)
            return receiver;
        if (TypeOf(receiver) is not { Type: var type } || type != BuiltInType.Xml)
            return NotModelled(call, "a method is called on a value that is not xml.");

        return method switch
        {
            XmlMethod.Value => XmlValue(call),
            XmlMethod.Exist => XmlExist(call, receiver),
            XmlMethod.Query => XmlQuery(call, receiver),
            _ => NotModelled(call, "XML's nodes() is a source in FROM, not a value."),
        };
    }

    /// <summary>
    /// What a method is called on: the value of <paramref name="target"/>, or, for a column <c>nodes()</c> gives, the node, which
    /// is never NULL itself (observed: A_nodes_column_is_never_null_and_is_read_only_by_xml_s_methods), and NULL only on an
    /// <c>OUTER APPLY</c>'s optional side, as any column there is. The server takes such a column only as a method's target or in
    /// <c>IS NULL</c>, so it is read only here (<see cref="NodeColumn"/>).
    /// </summary>
    private Value XmlReceiver(ScalarExpression target, ReadScope scope) =>
        target is ColumnReferenceExpression column && binder.Resolve(column, scope.Bound) is RelationColumn { Relation: XmlNodesCall } node
            ? FromLocal(node.Source, node.Ordinal, scope, At(column))
            : Expression(target, scope);

    /// <summary>
    /// A column <c>nodes()</c> gives, read as anything but a method's target: the server refuses it, in a select list, <c>*</c>,
    /// a conversion, <c>GROUP BY</c> or <c>COUNT</c> (493 and 525: observed), so it has no value to type.
    /// </summary>
    private static Value NodeColumn(string name, SourceLocation at) => Value.Unknown(new UnknownShape(
        IrUnknownReason.UnsupportedExpression,
        $"The column {name} that XML's nodes() returns can't be used directly: the server takes it only with an XML method, value(), exist(), query() or nodes(), or in IS NULL (493).",
        at));

    /// <summary>
    /// <c>x.query('path')</c>: untyped <c>xml</c>, whatever collection <c>x</c> is typed by, NULL exactly when <c>x</c> is; where the
    /// path finds nothing it is an empty instance, not NULL (observed: Query_is_untyped_xml_null_exactly_when_its_instance_is).
    /// The server takes only a literal (8172).
    /// </summary>
    private Value XmlQuery(FunctionCall call, Value receiver) =>
        call.Parameters is [StringLiteral]
            ? Typed(ScalarType.Of(BuiltInType.Xml), receiver.NullableBecause, call)
            : NotModelled(call, "XML's query() takes a literal path, which the server requires.");

    /// <summary>
    /// XML's <c>nodes('path') AS t(c)</c> in FROM: a row for each node the path finds, none for a NULL instance or a path that
    /// finds nothing, so any number for each outer row; an <c>OUTER APPLY</c> keeps the outer row with NULL, as for any source on
    /// its optional side. Its one column is the node, an <c>xml</c> value never NULL, which only XML's methods read (observed:
    /// A_nodes_column_is_never_null_and_is_read_only_by_xml_s_methods, Nodes_gives_a_row_for_each_node_and_none_for_null).
    /// </summary>
    private LocalRelationValues XmlNodes(XmlNodesCall symbol, ReadScope scope)
    {
        var at = At(symbol.Syntax);
        LocalRelationValues Fails(string detail) => LocalRelationValues.Unknown(Unsupported(IrUnknownReason.UnsupportedSource, $"{Text(symbol.Syntax)}: {detail}", at));

        if (symbol.Columns is not { } columns)
            return Fails("XML's nodes() gives one column, so its column list names one (8159).");
        if (symbol.Path is not [StringLiteral])
            return Fails("XML's nodes() takes a literal path, which the server requires (8172).");
        var receiver = XmlReceiver(symbol.Target, scope);
        if (receiver.Shape is UnknownShape unknown)
            return LocalRelationValues.Unknown(unknown);
        if (TypeOf(receiver) is not { Type: var type } || type != BuiltInType.Xml)
            return Fails("XML's nodes() is called on a value that is not xml (9506).");
        return new LocalRelationValues(columns, [Typed(ScalarType.Of(BuiltInType.Xml), [], symbol.Syntax)], null);
    }

    /// <summary>
    /// <c>x.value('path', 'type')</c>: a value of the type its literal second argument names, NULL where the path finds no node or
    /// <c>x</c> is NULL. The server takes only literals for both arguments (observed); a type it takes but turns into another,
    /// <c>json</c> and <c>vector(n)</c> into <c>varchar(max)</c>, and one inference doesn't model, stay unknown.
    /// </summary>
    private Value XmlValue(FunctionCall call)
    {
        if (call.Parameters is not [StringLiteral, StringLiteral { Value: var written }])
            return NotModelled(call, "XML's value() takes a literal path and a literal type, which the server requires.");
        if (XmlValueType(written) is not { } type)
            return NotModelled(call, $"XML's value() of the type '{written}' is not modelled.");

        // No proof that the path always finds a node is modelled, so it is always nullable.
        return Typed(type, [new IrNullReason(IrNullRule.XmlValue, At(call).ToString())], call);
    }

    /// <summary>
    /// <c>x.exist('path')</c>: a <c>bit</c>, 1 where the path finds a node and 0 where it finds none, an empty instance included,
    /// and NULL exactly when <c>x</c> is (observed, with a NULL <c>sql:variable</c> and <c>sql:column</c> in the path too).
    /// </summary>
    private Value XmlExist(FunctionCall call, Value receiver) =>
        call.Parameters is [StringLiteral]
            ? Typed(ScalarType.Of(BuiltInType.Bit), receiver.NullableBecause, call)
            : NotModelled(call, "XML's exist() takes a literal path, which the server requires.");

    /// <summary>
    /// The type <c>value()</c>'s type argument names, read as a declaration reads it (<c>nvarchar</c> is <c>nvarchar(1)</c>,
    /// <c>decimal</c> <c>decimal(18,0)</c>: observed): a built-in type's name, in any case, bracketed or in <c>sys</c>, with its
    /// arguments. Null for anything else, and for a type the server gives as another (<c>json</c>, <c>vector</c>), refuses
    /// (<c>xml</c>, <c>text</c>, <c>ntext</c>, <c>image</c>, <c>sql_variant</c>, the CLR types) or inference doesn't model.
    /// </summary>
    internal static ScalarType? XmlValueType(string written)
    {
        if (TypeArgument().Match(written) is not { Success: true } match)
            return null;
        var arguments = match.Groups["arguments"].Success ? "(" + string.Join(",", match.Groups["arguments"].Value.Split(',').Select(a => a.Trim())) + ")" : "";
        return SqlTypes.Parse(match.Groups["name"].Value + arguments) is { } type && type.Type != BuiltInType.Xml ? type : null;
    }

    /// <summary>A column reference of the parts of a method's target, placed where they are, so it binds and is located as written.</summary>
    private static ColumnReferenceExpression? AsColumn(MultiPartIdentifier parts)
    {
        if (parts.Identifiers.Count is < 1 or > 4)
            return null;
        return new ColumnReferenceExpression
        {
            ColumnType = ColumnType.Regular,
            MultiPartIdentifier = parts,
            FirstTokenIndex = parts.FirstTokenIndex,
            LastTokenIndex = parts.LastTokenIndex,
            ScriptTokenStream = parts.ScriptTokenStream,
        };
    }

    [GeneratedRegex(@"^\s*(?:(?:sys|\[sys\])\s*\.\s*)?\[?(?<name>[A-Za-z][A-Za-z0-9_]*)\]?\s*(?:\(\s*(?<arguments>[A-Za-z0-9 ,]+)\s*\))?\s*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex TypeArgument();
}
