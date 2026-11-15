using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Syntax;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Semantics;

// What a column reference means: the one resolver, and a write's column list.
public sealed partial class Binder
{
    /// <summary>
    /// What a column reference means in <paramref name="scope"/>: the one rule, SQL Server's, by which every column in a body is
    /// resolved. A qualified column is the column of the source its qualifier names in the innermost scope that has one: by its
    /// alias, or by its table's name when it has none (<c>t.id</c>), or by its full name when it has none (<c>dbo.t.id</c>,
    /// <see cref="ExposedName"/>). An unqualified one is the column of the one source of the innermost scope that has a column
    /// of its name, ambiguous when more than one there has it, and of a source whose columns are not known when none is known to.
    /// </summary>
    /// <param name="column">The reference.</param>
    /// <param name="scope">The scope of the query it is read in.</param>
    public Resolution Resolve(ColumnReferenceExpression column, QueryScope scope)
    {
        ArgumentNullException.ThrowIfNull(column);
        ArgumentNullException.ThrowIfNull(scope);
        var at = At(column);
        if (column.ColumnType != ColumnType.Regular || column.MultiPartIdentifier is null)
            return new UnmodelledReference(Problem(BindingProblemKind.UnsupportedReference, $"The column reference {FragmentText.OneLine(column)} is not modelled yet.", at));

        // SQL Server binds db.schema.table.column in the database the query runs in (observed:
        // A_four_part_column_reference_binds_in_this_database), so leaving it unknown claims less, never more. No corpus
        // haruspex is measured on writes one (docs/corpus-harvest.md).
        var parts = column.MultiPartIdentifier.Identifiers;
        if (parts.Count > 3)
            return new UnmodelledReference(Problem(BindingProblemKind.UnsupportedReference, $"The column reference {FragmentText.OneLine(column)} names its database, which is not modelled yet.", at));

        var name = parts[^1].Value;
        var qualifier = parts.Take(parts.Count - 1).ToList();

        // Innermost scope first: a correlated subquery sees its own sources before the outer query's.
        for (var s = scope; s is not null; s = s.Parent)
        {
            if (qualifier.Count > 0)
            {
                var named = Named(s.Sources, qualifier);
                if (named.Count > 1)
                    return new AmbiguousColumn(Problem(BindingProblemKind.UnresolvedColumn, Repeated(qualifier), at));
                if (named.Count == 1)
                    return FromSource(named[0], name, at);
                continue;
            }

            var matches = s.Sources.Where(x => Has(x, name)).ToList();
            if (matches.Count > 1)
                return new AmbiguousColumn(Problem(BindingProblemKind.UnresolvedColumn, $"The column {name} is ambiguous: more than one table in scope has it.", at));
            if (matches.Count == 1)
                return FromSource(matches[0], name, at);

            // A source that cannot be bound, or whose columns are not known, may be the one that has this column.
            if (s.Sources.FirstOrDefault(x => x.Target is UnresolvedTarget or RelationTarget { Relation.Columns: null }) is { } unknown)
                return FromSource(unknown, name, at);
        }

        // Observed: An_alias_hides_a_tables_full_name (4104).
        return new MissingColumn(Problem(BindingProblemKind.UnresolvedColumn, qualifier.Count == 2
            ? $"No source in scope is named {FullName(qualifier)} without an alias, as a column of three parts needs: an alias hides a table's own name."
            : $"No table in scope has the column {FragmentText.OneLine(column)}.", at));
    }

    /// <summary>
    /// The sources of one scope a qualifier of one or two parts names: by alias, or by table name when there is none
    /// (<see cref="BoundSource.Alias"/>); or by full name (<see cref="BoundSource.Exposed"/>). A source whose full name can't be
    /// told (it names another database the project doesn't hold, or a name whose schema is the caller's) is counted when its
    /// own name matches, so that a column is never taken from a source further out that SQL Server might not reach.
    /// </summary>
    private static List<BoundSource> Named(IEnumerable<BoundSource> sources, List<Identifier> qualifier) => qualifier switch
    {
        [var alias] => [.. sources.Where(s => alias.Value.Equals(s.Alias, StringComparison.OrdinalIgnoreCase))],
        [var schema, var table] => [.. sources.Where(s => s.Exposed is { } exposed
            ? exposed.IsNamed(schema.Value, table.Value)
            : s is { Target: UnresolvedTarget, Syntax: NamedTableReference { Alias: null, SchemaObject.BaseIdentifier.Value: var own } }
              && own.Equals(table.Value, StringComparison.OrdinalIgnoreCase))],
        _ => throw new ArgumentException($"A qualifier of {qualifier.Count} parts names no source of this database.", nameof(qualifier)),
    };

    /// <summary>
    /// Why a qualifier that names more than one source of a scope means none: SQL Server refuses such a FROM (observed:
    /// Two_sources_of_one_name_without_aliases_are_refused), so the query never runs.
    /// </summary>
    private static string Repeated(List<Identifier> qualifier) => qualifier.Count == 2
        ? $"Two sources in scope are named {FullName(qualifier)}, which SQL Server refuses (Msg 1013)."
        : $"Two sources in scope are named {qualifier[0].Value}, which SQL Server refuses.";

    /// <summary>A qualifier of two parts as T-SQL writes it, with a part bracketed where it must be.</summary>
    private static string FullName(List<Identifier> qualifier) => new ObjectName(qualifier[0].Value, qualifier[1].Value).ToString();

    /// <summary>Whether a source is known to have a column of this name.</summary>
    private static bool Has(BoundSource source, string name) => source.Target switch
    {
        TableTarget { Table: var table } => table.Column(name) is not null,
        RelationTarget { Relation: var relation } => relation.IndexOf(name) >= 0,
        UnresolvedTarget => false,
    };

    private static Resolution FromSource(BoundSource source, string name, SourceLocation at) => source.Target switch
    {
        RelationTarget { Relation: { Columns: null } relation } => new UnlistedColumn(source, relation),
        RelationTarget { Relation: var relation } => relation.IndexOf(name) is >= 0 and var ordinal
            ? new RelationColumn(source, relation, ordinal)
            : new MissingColumn(Problem(BindingProblemKind.UnresolvedColumn, $"{relation.Name} has no column {name}.", at)),

        // The source's own problem, moved to the column, so that each reference is reported where it is.
        UnresolvedTarget { Problem: var problem } => new UnboundSource(source, problem with { At = at }),
        TableTarget { Table: var table } => table.Column(name) is { } column
            ? new TableColumn(source, table, column)
            : new MissingColumn(Problem(BindingProblemKind.UnresolvedColumn, $"The table {table.Name} has no column {name}.", at)),
    };

    /// <summary>
    /// What a variable reference means where <paramref name="names"/> are in force: a parameter of the body, a variable it
    /// declares, or neither, which SQL Server refuses.
    /// </summary>
    /// <param name="variable">The reference.</param>
    /// <param name="names">The names in force, whose declarations are the body's.</param>
    public static VariableResolution Resolve(VariableReference variable, NameEnvironment names)
    {
        ArgumentNullException.ThrowIfNull(variable);
        ArgumentNullException.ThrowIfNull(names);
        return names.Declarations.Variable(new LocalName(variable.Name));
    }

    /// <summary>
    /// The position, among a write's target's columns, of each column its column list names (<c>INSERT … (a, b)</c>,
    /// <c>OUTPUT … INTO t (a, b)</c>), by its last part, ignoring case as SQL Server does; -1 for one the target doesn't have.
    /// </summary>
    /// <param name="listed">The column list.</param>
    /// <param name="columns">The target's columns, in order.</param>
    public static IReadOnlyList<int> Written(IEnumerable<ColumnReferenceExpression> listed, IReadOnlyList<string> columns)
    {
        ArgumentNullException.ThrowIfNull(listed);
        ArgumentNullException.ThrowIfNull(columns);
        return [.. listed.Select(c => columns.ToList().FindIndex(n => n.Equals(WrittenColumn(c), StringComparison.OrdinalIgnoreCase)))];
    }

    /// <summary>The name of a column a write sets (<c>SET t.a = …</c>) or lists: its last part.</summary>
    /// <param name="column">The column as written.</param>
    public static string WrittenColumn(ColumnReferenceExpression column)
    {
        ArgumentNullException.ThrowIfNull(column);
        return column.MultiPartIdentifier.Identifiers[^1].Value;
    }
}
