using Haruspex.Ir;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

// Slice 6e: VALUES lists, views, and the SELECT * a view binds when it is created. APPLY itself is binding's (Binder.cs) and
// refinement's (Refinement.cs, Cardinality.cs); README.md in this folder ("APPLY", "Views") gives the rules and the
// observations they rest on (ApplyViewObservationTests in the differential tests).
internal sealed partial class ProcedureAnalyzer
{
    /// <summary>Whether <c>SELECT *</c> is expanded: only in a view, whose star SQL Server binds when it creates it.</summary>
    private bool expandStars;

    /// <summary>The body's relations, with derived tables, VALUES lists and views read here.</summary>
    private FlowLocals Locals(IReadOnlyDictionary<string, LocalRelationValues> tableVariables) => new(tableVariables)
    {
        ReadDerived = Derived,
        ReadValues = ValuesList,
        ReadView = views is null ? null : views.Read,
    };

    /// <summary>
    /// Whether a source on an OUTER APPLY's right side is never null-extended: when it is the whole right side and always
    /// has a row (observed: An_ungrouped_aggregate_applied_gives_one_row_for_every_outer_row,
    /// A_for_json_subquery_applied_without_from_is_one_row_whose_json_is_null_when_there_are_no_rows,
    /// A_values_list_gives_one_row_for_each_of_its_rows_for_every_outer_row). A relation joined inside the right side is
    /// not enough: the join may still leave the side without a row.
    /// </summary>
    private bool AlwaysApplied(BoundLocalColumn bound) =>
        bound.Source.WholeApplied && locals!.Values(bound.Relation).AtLeastOne;

    // ---- VALUES ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A VALUES list, <c>(VALUES (a, b), (c, d)) AS v (x, y)</c>: as many rows as it lists, each time it is read (for an APPLY's
    /// right side, for each outer row), so always at least one. A column holds its rows' values, null when any may be. Its
    /// type is the one of highest precedence among its rows', to which every value is converted (observed:
    /// A_values_column_takes_the_type_of_highest_precedence_among_its_rows), which is not modelled: so a column is typed only
    /// when its rows' values all have the same shape, whose claims then hold of each value whatever the type, or when there
    /// is one row. JSON passes through as JSON (observed: Json_passes_through_a_values_list_as_json).
    /// </summary>
    private LocalRelationValues ValuesList(InlineDerivedTable table, BindingScope? outer)
    {
        var at = At(table);
        var name = table.Alias?.Value ?? "(VALUES)";
        LocalRelationValues Fails(string detail) => LocalRelationValues.Unknown(Unsupported(IrUnknownReasons.UnsupportedQuery, $"{name}: {detail}", at));

        // SQL Server requires a name for every column (8155) and the same number of values in every row (10709).
        if (table.Columns.Count == 0 || table.RowValues.Any(r => r.ColumnValues.Count != table.Columns.Count))
            return Fails("a VALUES list needs a name for each column and as many values in each row.");
        var names = table.Columns.Select(c => c.Value).ToList();
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count)
            return Fails("its column names are not distinct.");

        // Its rows are read with no FROM of their own: an enclosing query's columns, or an APPLY's left input's.
        var scope = binder.BindFrom(new QuerySpecification(), outer, outer is null ? locals : null);
        var rows = table.RowValues.Select(r => r.ColumnValues.Select(v => Expression(v, scope)).ToList()).ToList();
        var values = new List<Value>();
        for (var i = 0; i < names.Count; i++)
        {
            var column = rows.Select(r => r[i]).ToList();
            if (column.FirstOrDefault(v => v.Shape is UnknownShape) is { } unknown)
            {
                values.Add(unknown);
                continue;
            }

            var first = column[0];
            var same = column.All(v => v.IsLiveJson == first.IsLiveJson && ShapeText.Of(v.Shape) == ShapeText.Of(first.Shape));
            values.Add(same
                ? first with { NullableBecause = column.Select(v => v.NullableBecause).Aggregate(ShapeJoin.Reasons) }
                : Value.Unknown(Unsupported(
                    IrUnknownReasons.UnsupportedExpression,
                    $"{name}.{names[i]}: its rows' values have different types, which SQL Server converts to the one of highest precedence; that is not modelled yet.",
                    at)));
        }

        return new LocalRelationValues(names, values, null)
        {
            Rows = rows.Count == 1 ? IrCardinality.One : IrCardinality.Many,
            AtLeastOne = true,
        };
    }

    // ---- views --------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A view's columns, read from its definition as a derived table would be, with its own file, its own ANSI_NULLS (observed:
    /// A_view_keeps_ansi_nulls_as_it_was_when_it_was_created) and none of the reader's relations. Its column list names its
    /// columns (observed: A_view_column_list_names_every_column_and_no_more). Table hints such as NOEXPAND change how it is
    /// read, not what it returns (observed: Noexpand_is_refused_on_a_view_without_an_index_and_on_one_with_it_returns_the_same_rows),
    /// and SCHEMABINDING only stops its tables changing under it.
    /// </summary>
    /// <remarks>
    /// <c>SELECT *</c> in a view is bound when the view is created, and stays so until the view is refreshed (observed:
    /// A_view_binds_select_star_when_it_is_created). The project shows each table as it is after deployment, and haruspex
    /// assumes each view is bound against that: created after its tables reached their final form, or refreshed since, which
    /// is what a deployment tool that recreates or refreshes dependent views gives. A view left bound to an older form of a
    /// table is not described.
    /// </remarks>
    public LocalRelationValues ReadView(NamedObject named)
    {
        ArgumentNullException.ThrowIfNull(named);
        var view = named.View ?? throw new ArgumentException("The view has no single definition.", nameof(named));
        expandStars = true;
        locals = Locals(new Dictionary<string, LocalRelationValues>(StringComparer.OrdinalIgnoreCase));
        var at = At(view);
        LocalRelationValues Fails(string detail) => LocalRelationValues.Unknown(Unsupported(IrUnknownReasons.UnsupportedQuery, $"{named.Name}: {detail}", at));

        var select = view.SelectStatement;
        return WithCtes(
            select.WithCtesAndXmlNamespaces,
            () =>
            {
                if (select.QueryExpression is not QuerySpecification { ForClause: null } query)
                    return Fails("a view whose query is a UNION, INTERSECT, EXCEPT or parenthesised query, or FOR XML or FOR JSON, is not modelled yet.");
                var row = Query(query, parent: null);
                return Relation(view.Columns, row) is { } relation
                    ? relation with { Rows = row.Rows, AtLeastOne = row.Rows == IrCardinality.One, Keys = ViewKeys(query, row) }
                    : Fails("its columns cannot be listed: a SELECT * over a source whose columns are not known, or over a table whose * differs from its columns (hidden or graph columns), or a column without a name of its own.");
            },
            LocalRelationValues.Unknown);
    }

    /// <summary>
    /// The keys a view has: those of its one table that it selects whole, unchanged. A view that reads one table, without a
    /// join, APPLY or GROUP BY, returns some of the table's rows, each once (observed:
    /// A_view_over_one_table_returns_each_of_its_rows_at_most_once), so no two of its rows share a key the table enforces.
    /// WHERE, TOP and DISTINCT only drop rows. GROUP BY is left out, though grouping by a whole key keeps it, since ROLLUP and
    /// its kind add rows that would share it; a join is left out, though one that matches at most one row keeps the other
    /// side's keys, because proving that needs the joined side's key fixed by the ON, and views rarely need it.
    /// </summary>
    private List<RelationKey> ViewKeys(QuerySpecification query, Row row)
    {
        if (query.GroupByClause is not null || row.Sources is not [{ Table: { } table, OptionalSideOf: null } source])
            return [];

        var keys = new List<RelationKey>();
        foreach (var key in SchemaRules.EnforcedKeys(table))
        {
            var ordinals = new List<int>();
            var types = new List<string>();
            foreach (var name in key.Columns)
            {
                var ordinal = row.Columns.ToList().FindIndex(c => c.Origin is { } origin && ReferenceEquals(origin.Source, source)
                    && origin.Column.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (ordinal < 0 || SchemaRules.ResolveType(model, table.Column(name)?.Type)?.BuiltIn.Name is not { } type)
                    break;
                ordinals.Add(ordinal);
                types.Add(type);
            }

            if (ordinals.Count == key.Columns.Count)
                keys.Add(new RelationKey(ordinals, types));
        }

        return keys;
    }

    // ---- SELECT * ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The columns <c>*</c> or <c>alias.*</c> stands for: each source's columns in order, a table's as declared, a relation's
    /// as it names them. Null when one of them is not known, or a table's <c>*</c> differs from its declared columns: a hidden
    /// period column is left out of it, and a graph table adds pseudo-columns.
    /// </summary>
    private List<RowColumn>? Expand(SelectStarExpression star, BindingScope scope)
    {
        var at = At(star);
        IReadOnlyList<BoundSource> sources = scope.Sources;
        if (star.Qualifier is { } qualifier)
        {
            if (qualifier.Identifiers.Count != 1)
                return null;
            sources = [.. scope.Sources.Where(s => qualifier.Identifiers[0].Value.Equals(s.Alias, StringComparison.OrdinalIgnoreCase))];
            if (sources.Count != 1)
                return null;
        }

        var columns = new List<RowColumn>();
        foreach (var source in sources)
        {
            if (source.Table is { } table)
            {
                if (table.IsGraph || table.Columns.Any(c => c.IsHidden))
                    return null;
                foreach (var column in table.Columns)
                {
                    var bound = new BoundColumn(source, table, column);
                    columns.Add(new RowColumn(column.Name, FromColumn(bound), at, bound));
                }
            }
            else if (source.Local is { Columns: { } names } local)
            {
                for (var i = 0; i < names.Count; i++)
                    columns.Add(new RowColumn(names[i], FromLocal(new BoundLocalColumn(source, local, i), at), at));
            }
            else
            {
                return null;
            }
        }

        return columns;
    }
}

/// <summary>
/// What each view of the project holds, read once per document from its definition (<see cref="ProcedureAnalyzer.ReadView"/>).
/// A view reads others; one that reads itself through them cannot be created, and is unknown here.
/// </summary>
internal sealed class ViewSummaries(DatabaseModel model, FunctionSummaries functions)
{
    private readonly Dictionary<ObjectName, LocalRelationValues> done = [];
    private readonly HashSet<ObjectName> reading = [];

    /// <summary>A placeholder location for an unknown about the view as a whole; each read reports its own location instead.</summary>
    private static readonly Haruspex.Diagnostics.SourceLocation Nowhere = new("", 0, 0);

    public LocalRelationValues Read(NamedObject view)
    {
        if (done.TryGetValue(view.Name, out var found))
            return found;

        // A view created and altered, or defined twice, depends on the order the scripts run (Sources).
        if (view.View is null || view.File is null)
            return LocalRelationValues.Unknown(new UnknownShape(IrUnknownReasons.UnresolvedTable, $"{view.Name} is defined more than once, and which definition applies depends on the order the scripts run.", Nowhere));
        if (!reading.Add(view.Name))
            return LocalRelationValues.Unknown(new UnknownShape(IrUnknownReasons.UnresolvedTable, $"{view.Name} reads itself through the views it reads, which SQL Server refuses.", Nowhere));

        try
        {
            found = new ProcedureAnalyzer(model, view.File, functions, ansiNullsOff: view.AnsiNullsOff, views: this).ReadView(view);
        }
        finally
        {
            reading.Remove(view.Name);
        }

        done[view.Name] = found;
        return found;
    }
}
