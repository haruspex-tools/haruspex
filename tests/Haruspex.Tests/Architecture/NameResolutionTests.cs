using Haruspex.SqlServer.Inference;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.Tests.Architecture;

/// <summary>
/// Names are resolved in Semantics only: which source a column reference or a qualifier means, and which relation a name in
/// FROM reads, is the binder's to say (<see cref="Binder.Resolve(ColumnReferenceExpression, QueryScope)"/>, <see cref="Binder.Bind"/>), and Inference attaches
/// values to what it bound. So no method of Inference reads the parts a resolver reads, as read from its IL.
/// </summary>
public class NameResolutionTests
{
    /// <summary>
    /// The members a resolver reads: a source's alias, a column reference's parts, a table reference's name or alias, a schema
    /// object name's parts, and the names a body declares (a variable, a table variable, a cursor) or a statement gives a cursor.
    /// </summary>
    private static readonly string[] ResolverReads =
    [
        $"{typeof(BoundSource).FullName}.get_{nameof(BoundSource.Alias)}",
        $"{typeof(ColumnReferenceExpression).FullName}.get_{nameof(ColumnReferenceExpression.MultiPartIdentifier)}",
        $"{typeof(NamedTableReference).FullName}.get_{nameof(NamedTableReference.SchemaObject)}",
        $"{typeof(TableReferenceWithAlias).FullName}.get_{nameof(TableReferenceWithAlias.Alias)}",
        $"{typeof(SchemaObjectName).FullName}.get_{nameof(SchemaObjectName.BaseIdentifier)}",
        $"{typeof(SchemaObjectName).FullName}.get_{nameof(SchemaObjectName.SchemaIdentifier)}",
        $"{typeof(SchemaObjectName).FullName}.get_{nameof(SchemaObjectName.DatabaseIdentifier)}",
        $"{typeof(SchemaObjectName).FullName}.get_{nameof(SchemaObjectName.ServerIdentifier)}",
        $"{typeof(DeclareVariableElement).FullName}.get_{nameof(DeclareVariableElement.VariableName)}",
        $"{typeof(DeclareTableVariableBody).FullName}.get_{nameof(DeclareTableVariableBody.VariableName)}",
        $"{typeof(DeclareCursorStatement).FullName}.get_{nameof(DeclareCursorStatement.Name)}",
        $"{typeof(CursorId).FullName}.get_{nameof(CursorId.Name)}",
    ];

    /// <summary>
    /// The methods of Inference that read a schema object name's parts or a declared variable's name, and why: none finds a
    /// source, a column or a variable. Each says what
    /// it reads the name for; the test fails on a read not listed, and on a listing that reads nothing any more.
    /// </summary>
    private static readonly Dictionary<string, string> NameParts = new(StringComparer.Ordinal)
    {
        ["FunctionSummaries.Returned"] = "A scalar function's straight-line body read as one expression: each variable's last value substituted in the order the body declares and assigns them, not a name resolved.",
        ["DmlTriggers.Reaches"] = "The tables a view's definition names, whose triggers a write through the view fires; schema names get one rule of their own next.",
    };

    [Fact]
    public void Inference_reads_no_name_a_resolver_reads()
    {
        var reads = MemberReads.Of(typeof(IrInference).Assembly.Location, _ => true)
            .Where(r => ResolverReads.Any(m => r.EndsWith(": " + m, StringComparison.Ordinal)))
            .ToList();
        var unlisted = reads.Where(r => !NameParts.ContainsKey(r[..r.IndexOf(':', StringComparison.Ordinal)]) || !(r.Contains(".SchemaObjectName.", StringComparison.Ordinal) || r.Contains(".DeclareVariableElement.", StringComparison.Ordinal)));
        var stale = NameParts.Keys.Where(m => !reads.Any(r => r.StartsWith(m + ":", StringComparison.Ordinal)));

        Assert.True(!unlisted.Any(), "Inference reads what only binding should:\n" + string.Join("\n", unlisted));
        Assert.True(!stale.Any(), "Listed as reading a name's parts, but no longer does:\n" + string.Join("\n", stale));
    }

    [Fact]
    public void The_check_finds_a_read_in_a_method_a_lambda_an_iterator_and_a_local_function()
    {
        // So that the check can't pass by seeing nothing: each place a compiler puts code is planted once.
        var reads = MemberReads.Of(typeof(NameResolutionTests).Assembly.Location, t => t == nameof(Planted))
            .Where(r => ResolverReads.Any(m => r.EndsWith(": " + m, StringComparison.Ordinal)))
            .ToList();

        Assert.Equal(
            [
                $"Planted.Alias: {ResolverReads[0]}",
                $"Planted.Parts: {ResolverReads[1]}",
                $"Planted.Schemas: {ResolverReads[2]}",
                $"Planted.TableAlias: {ResolverReads[3]}",
                $"Planted.TableName: {ResolverReads[4]}",
            ],
            reads);
    }

    /// <summary>Reads each member a resolver reads, as Inference must not; only <see cref="The_check_finds_a_read_in_a_method_a_lambda_an_iterator_and_a_local_function"/> reads it.</summary>
    private static class Planted
    {
        public static string? Alias(BoundSource source) => source.Alias;

        public static List<MultiPartIdentifier> Parts(IEnumerable<ColumnReferenceExpression> columns) => [.. columns.Select(c => c.MultiPartIdentifier)];

        public static IEnumerable<SchemaObjectName> Schemas(IEnumerable<NamedTableReference> tables)
        {
            foreach (var table in tables)
                yield return table.SchemaObject;
        }

        public static string? TableAlias(TableReferenceWithAlias table)
        {
            return Named();

            string? Named() => table.Alias?.Value;
        }

        public static bool TableName(IEnumerable<SchemaObjectName> names, string name) =>
            names.Any(n => n.BaseIdentifier.Value.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}
