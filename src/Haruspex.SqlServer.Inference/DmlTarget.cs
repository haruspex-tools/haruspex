using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// What a statement that writes a table is aimed at, as written: the permanent tables or views it may change (through an
/// alias in FROM, a CTE or a derived table, each name it reads, which is more than it changes and so claims less), the
/// temporary tables it changes through a CTE, and the temporary table or table variable it writes directly.
/// </summary>
/// <param name="Objects">The permanent objects it may change, as written.</param>
/// <param name="Temps">Temporary tables it may change through a CTE or derived table, which then hold what is not followed.</param>
/// <param name="Direct">The temporary table (<c>#t</c>) or table variable (<c>@t</c>) it writes directly, if it does.</param>
/// <param name="Unmodelled">Why the target is of a form not modelled (<c>OPENROWSET</c>, <c>OPENQUERY</c>, …), if it is.</param>
internal sealed record DmlTarget(IReadOnlyList<SchemaObjectName> Objects, IReadOnlyList<string> Temps, string? Direct, string? Unmodelled)
{
    /// <summary>
    /// The permanent object the write changes when it names one, directly or through an alias in FROM, rather than through a
    /// CTE or derived table, whose columns are not its object's: what <c>inserted</c> and <c>deleted</c> are rows of.
    /// </summary>
    public SchemaObjectName? Named { get; init; }

    /// <summary>
    /// The target of a write: <paramref name="target"/> as written, an alias of a source in <paramref name="from"/>
    /// (<c>UPDATE x … FROM t AS x</c>), or a CTE of <paramref name="with"/>.
    /// </summary>
    public static DmlTarget Of(TableReference target, FromClause? from, WithCtesAndXmlNamespaces? with)
    {
        var objects = new List<SchemaObjectName>();
        var temps = new List<string>();
        string? direct = null;
        string? unmodelled = null;
        SchemaObjectName? namedObject = null;
        var ctes = with?.CommonTableExpressions.ToDictionary(c => c.ExpressionName.Value, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, CommonTableExpression>(StringComparer.OrdinalIgnoreCase);
        Resolve(target, aliases: true, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        return new DmlTarget(objects, temps, direct, unmodelled) { Named = objects.Count == 1 && temps.Count == 0 && direct is null ? namedObject : null };

        void Resolve(TableReference reference, bool aliases, HashSet<string> seen)
        {
            switch (reference)
            {
                case VariableTableReference variable:
                    direct ??= variable.Variable.Name;
                    break;
                case NamedTableReference { SchemaObject: { SchemaIdentifier: null, DatabaseIdentifier: null, ServerIdentifier: null } name } named:
                    var one = name.BaseIdentifier.Value;

                    // UPDATE x … FROM t AS x: the target is the source of that alias. A source without an alias is its own name.
                    if (aliases && from is not null && Sources(from.TableReferences).FirstOrDefault(s => one.Equals(Alias(s), StringComparison.OrdinalIgnoreCase)) is { } aliased
                        && !ReferenceEquals(aliased, named))
                    {
                        Resolve(aliased, aliases: false, seen);
                        break;
                    }

                    if (ctes.TryGetValue(one, out var cte))
                    {
                        if (seen.Add(one))
                            Read(cte.QueryExpression, seen);
                        break;
                    }

                    if (one.StartsWith('#') && !one.StartsWith("##", StringComparison.Ordinal))
                    {
                        direct ??= one;
                    }
                    else
                    {
                        objects.Add(name);
                        namedObject ??= name;
                    }

                    break;
                case NamedTableReference other:
                    objects.Add(other.SchemaObject);
                    namedObject ??= other.SchemaObject;
                    break;
                case QueryDerivedTable derived:
                    Read(derived.QueryExpression, seen);
                    break;
                default:
                    unmodelled ??= $"A write to {FragmentText.OneLine(reference)} is not modelled yet.";
                    break;
            }
        }

        // Every table a CTE's or derived table's query names: one of them is the table the write changes underneath.
        void Read(QueryExpression query, HashSet<string> seen)
        {
            var names = new NamedTables();
            query.Accept(names);
            foreach (var name in names.Found)
            {
                var one = name.BaseIdentifier.Value;
                if (name is { SchemaIdentifier: null, DatabaseIdentifier: null, ServerIdentifier: null } && ctes.TryGetValue(one, out var cte))
                {
                    if (seen.Add(one))
                        Read(cte.QueryExpression, seen);
                }
                else if (name is { SchemaIdentifier: null, DatabaseIdentifier: null, ServerIdentifier: null } && one.StartsWith('#') && !one.StartsWith("##", StringComparison.Ordinal))
                {
                    temps.Add(one);
                }
                else
                {
                    objects.Add(name);
                }
            }
        }
    }

    /// <summary>The name a source's columns are qualified by: its alias, or its own name without one.</summary>
    public static string? Alias(TableReferenceWithAlias source) =>
        source.Alias?.Value ?? (source as NamedTableReference)?.SchemaObject.BaseIdentifier.Value ?? (source as VariableTableReference)?.Variable.Name;

    /// <summary>The sources of a FROM clause, through its joins.</summary>
    public static IEnumerable<TableReferenceWithAlias> Sources(IEnumerable<TableReference> references)
    {
        foreach (var reference in references)
        {
            switch (reference)
            {
                case JoinTableReference join:
                    foreach (var source in Sources([join.FirstTableReference, join.SecondTableReference]))
                        yield return source;
                    break;
                case JoinParenthesisTableReference parenthesis:
                    foreach (var source in Sources([parenthesis.Join]))
                        yield return source;
                    break;
                case TableReferenceWithAlias aliased:
                    yield return aliased;
                    break;
            }
        }
    }

    /// <summary>Every table name a fragment reads, at any depth.</summary>
    internal sealed class NamedTables : TSqlFragmentVisitor
    {
        public List<SchemaObjectName> Found { get; } = [];

        public override void Visit(NamedTableReference node) => Found.Add(node.SchemaObject);
    }
}
