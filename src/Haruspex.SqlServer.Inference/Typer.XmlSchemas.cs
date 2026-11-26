using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Model.XmlSchemas;
using Haruspex.SqlServer.Semantics;
using Haruspex.SqlServer.Semantics.XmlSchemas;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// What an XML method's path reaches in its instance's schema collection: SQL Server validates a typed instance on every write, so
/// a node the schemas require is there, and a value has its simple type's facets. Only a table's column, a variable or a parameter
/// declared <c>xml(collection)</c>, and the nodes <c>nodes()</c> gives over one, are read so. Why:
/// docs/decisions/0139-xml-is-typed-from-its-schema-collection-where-the-schema-proves-it.md.
/// </summary>
internal sealed partial class Typer
{
    /// <summary>
    /// What the path among <paramref name="arguments"/> reaches in the instance <paramref name="target"/> is, or null when nothing is proved: the target
    /// isn't a typed instance, or a node of one, its collection's schemas aren't known, or the path isn't one that is followed.
    /// </summary>
    private XmlReach? XmlPathReach(ScalarExpression target, IReadOnlyList<ScalarExpression> arguments, ReadScope scope) =>
        arguments is [StringLiteral { Value: var text }, ..] && XmlPath.Parse(text) is { } path && XmlStartOf(target, scope) is var (schemas, start)
            && !UsesXmlNamespaces(target)
            ? XmlPaths.Reach(schemas, start, path)
            : null;

    /// <summary>The collection and the place a path in <paramref name="target"/> starts from, or null when it isn't a typed instance or a node of one.</summary>
    private (XmlSchemaDeclarations Schemas, XmlStart Start)? XmlStartOf(ScalarExpression target, ReadScope scope)
    {
        SqlType? Declared() => Strip(target) switch
        {
            ColumnReferenceExpression column => binder.Resolve(column, scope.Bound) is TableColumn { Column.Type: var type } ? type : null,
            VariableReference variable => Binder.Resolve(variable, scope.Names) switch
            {
                ParameterVariable parameter => parameter.Parameter.Type,
                LocalVariable local => local.Type,
                _ => null,
            },
            _ => null,
        };

        // A node nodes() gives is there, and of the declaration its path reaches.
        if (Strip(target) is ColumnReferenceExpression node && binder.Resolve(node, scope.Bound) is RelationColumn { Relation: XmlNodesCall nodes })
        {
            var inner = scope.In(nodes.Arguments);
            return XmlStartOf(nodes.Target, inner) is var (schemas, _) && XmlPathReach(nodes.Target, nodes.Path, inner) is { Node: { } reached }
                ? (schemas, new XmlNodeStart(reached))
                : null;
        }

        return Declared() is { Collection: { } reference } declared && SchemaRules.ResolveXmlCollection(model, declared) is { Declarations: { } declarations }
            ? (declarations, new XmlInstanceStart(reference.Document))
            : null;
    }

    /// <summary>
    /// Whether the batch <paramref name="at"/> is in uses <c>WITH XMLNAMESPACES</c>, which binds prefixes and a default element
    /// namespace that a path's own prolog doesn't show, so that no path in it is followed. The batch is the text between two
    /// <c>GO</c>s, a module's whole body; a statement's own <c>WITH</c> isn't found by itself, so this asks of the whole batch.
    /// </summary>
    private static bool UsesXmlNamespaces(TSqlFragment at)
    {
        var tokens = at.ScriptTokenStream;
        if (tokens is null || at.FirstTokenIndex < 0)
            return true;
        bool Uses(int i) => tokens[i].TokenType == TSqlTokenType.Identifier && tokens[i].Text.Equals("XMLNAMESPACES", StringComparison.OrdinalIgnoreCase);
        for (var i = at.FirstTokenIndex; i >= 0 && tokens[i].TokenType != TSqlTokenType.Go; i--)
        {
            if (Uses(i))
                return true;
        }

        for (var i = at.FirstTokenIndex; i < tokens.Count && tokens[i].TokenType != TSqlTokenType.Go; i++)
        {
            if (Uses(i))
                return true;
        }

        return false;
    }
}
