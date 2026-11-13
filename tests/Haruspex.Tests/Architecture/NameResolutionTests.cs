using Haruspex.SqlServer.Inference;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.Tests.Architecture;

/// <summary>
/// Names are resolved in Semantics only: which source a column reference or a qualifier means, and which relation a name in
/// FROM reads, is the binder's to say (<see cref="Binder.Resolve"/>, <see cref="Binder.Bind"/>), and Inference attaches
/// values to what it bound. So no method of Inference reads the parts a resolver reads, as read from its IL.
/// </summary>
public class NameResolutionTests
{
    /// <summary>The members a resolver reads: a source's alias, a column reference's parts, a table reference's name or alias.</summary>
    private static readonly string[] ResolverReads =
    [
        $"{typeof(BoundSource).FullName}.get_{nameof(BoundSource.Alias)}",
        $"{typeof(ColumnReferenceExpression).FullName}.get_{nameof(ColumnReferenceExpression.MultiPartIdentifier)}",
        $"{typeof(NamedTableReference).FullName}.get_{nameof(NamedTableReference.SchemaObject)}",
        $"{typeof(TableReferenceWithAlias).FullName}.get_{nameof(TableReferenceWithAlias.Alias)}",
    ];

    [Fact]
    public void Inference_reads_no_name_a_resolver_reads()
    {
        var reads = MemberReads.Of(typeof(IrInference).Assembly.Location, _ => true)
            .Where(r => ResolverReads.Any(m => r.EndsWith(": " + m, StringComparison.Ordinal)))
            .ToList();

        Assert.True(reads.Count == 0, "Inference reads what only binding should:\n" + string.Join("\n", reads));
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
    }
}
