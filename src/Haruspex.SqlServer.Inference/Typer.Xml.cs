using System.Text.RegularExpressions;
using Haruspex.Ir;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// The methods of an <c>xml</c> value: <c>x.value('path', 'type')</c> typed by its type argument and always nullable,
/// <c>x.exist('path')</c> a <c>bit</c> that is NULL exactly when <c>x</c> is, and <c>x.query()</c> and <c>x.nodes()</c> unknown.
/// Every rule was observed on SQL Server 2025 CU10 (<c>XmlObservationTests</c>). Why:
/// docs/decisions/0133-an-xml-value-is-a-kind-of-its-own-a-branded-string.md.
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

        var receiver = Expression(target, scope);
        if (receiver.Shape is UnknownShape)
            return receiver;
        if (TypeOf(receiver) is not { Type: var type } || type != BuiltInType.Xml)
            return NotModelled(call, "a method is called on a value that is not xml.");

        return method switch
        {
            XmlMethod.Value => XmlValue(call),
            XmlMethod.Exist => XmlExist(call, receiver),
            XmlMethod.Nodes => NotModelled(call, "XML's nodes() is a source in FROM, not a value."),
            _ => NotModelled(call, "XML's query() is not modelled yet: what it returns is XML its path shapes, which a schema collection may later type."),
        };
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
