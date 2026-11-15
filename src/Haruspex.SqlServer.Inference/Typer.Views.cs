using Haruspex.Collections;
using Haruspex.Ir;
using Haruspex.SqlServer.Inference.Lattice;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;
using Haruspex.SqlServer.Syntax;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

// VALUES lists and views. APPLY itself is binding's (Binder.cs) and refinement's (Typer.Refinement.cs, Cardinality.cs);
// README.md in this folder ("APPLY", "Views") gives the rules and the observations they rest on (ApplyViewObservationTests
// in the differential tests). A view's SELECT * is Typer.Star.cs's, as every query's is.
internal sealed partial class Typer
{
    // ---- what the relations a query reads hold ------------------------------------------------------------------------

    /// <summary>
    /// A query's scope with what each of its sources' relations holds attached, in the order the sources are written, so that
    /// an APPLY's right side reads its left input's as they are attached. Binding said which relation each source reads; this
    /// reads it.
    /// </summary>
    internal ReadScope Attach(ReadScope scope)
    {
        foreach (var source in scope.Bound.Sources)
        {
            if (source.Target is RelationTarget { Relation: var relation })
                scope = scope.Attached(source, Holding(relation, source.Uncommitted, scope));
        }

        return scope;
    }

    /// <summary>
    /// The scope of a statement's query, a specification read as the statement's own (not as a subquery), with what its
    /// relations hold attached.
    /// </summary>
    internal ReadScope Enter(QuerySpecification query, ReadScope at) =>
        Attach(at.In(((BoundSpecification)binder.Bind(query, null, at.Names)).Scope));

    /// <summary>What a relation holds where it is read in <paramref name="scope"/>.</summary>
    /// <param name="relation">The relation.</param>
    /// <param name="hinted">Whether it is read with a NOLOCK or READUNCOMMITTED hint, which a view passes to every table under it.</param>
    /// <param name="scope">The scope it is read in.</param>
    internal LocalRelationValues Holding(RelationSymbol relation, bool hinted, ReadScope scope) => relation switch
    {
        CommonTable or PseudoTable => scope.Named.TryGetValue(relation, out var named)
            ? named
            : throw new InvalidOperationException($"{relation.Name} is bound, but nothing it holds was attached."),
        DerivedTable derived => Derived(derived, scope),
        ValuesTable values => ValuesList(values, scope.In(values.Rows)),
        ViewRelation { View: var view } => summaries.View(view, hinted || ReadUncommitted(scope)),
        TableFunctionCall call => TableFunction(call, scope.In(call.Arguments)),
        OpenJsonCall openJson => OpenJsonRelation(openJson, scope.In(openJson.Arguments)),
        SeriesCall series => GenerateSeries(series, scope.In(series.Arguments)),
        TemporaryTable temporary => Temporary(temporary.Local, scope.State),
        TableVariable variable => AsWritten(variable.Local, TableVariableValues(variable.Declaration), scope.State),
    };

    /// <summary>What a temporary table holds in <paramref name="state"/>, or why it is not known there.</summary>
    private static LocalRelationValues Temporary(LocalName name, FlowState state)
    {
        // A global temporary table (##t) is shared with every session, which may change it at any time.
        if (name.Text.StartsWith("##", StringComparison.Ordinal))
            return LocalRelationValues.Unknown(new UnknownShape(IrUnknownReason.UnsupportedSource, $"{name} is a global temporary table, which any session may change.", Nowhere));

        return state.Temp(name) switch
        {
            KnownTable known => new LocalRelationValues([.. known.Columns.Select(c => c.Name)], [.. known.Columns.Select(c => c.Read)], null),
            UnknownTable unknown => LocalRelationValues.Unknown(unknown.Why),
            AbsentTable absent => LocalRelationValues.Unknown(new UnknownShape(IrUnknownReason.UnresolvedTable, $"{name} was dropped at {absent.At}, so it is not there to read.", absent.At)),

            // Not in the state at all: the procedure has not created it on this path.
            null => LocalRelationValues.Unknown(new UnknownShape(
                IrUnknownReason.CallerTempTable,
                $"{name} is not created by the procedure before this, so it is the caller's, whose shape depends on who calls.",
                Nowhere)),
        };
    }

    /// <summary>A table variable's columns as they stand in <paramref name="state"/>: each declared, holding the JSON written to it.</summary>
    private static LocalRelationValues AsWritten(LocalName name, LocalRelationValues declared, FlowState state) =>
        declared is not { Names: { } columns, Values: { } values } ? declared : declared with
        {
            Values = [.. columns.Select((c, i) => state.Json.TryGetValue(new ColumnSlot(name, c), out var slot) ? slot.Read(values[i]) : values[i])],
        };

    /// <summary>A placeholder location for an unknown that has none of its own; each read reports its own location instead.</summary>
    private static readonly Diagnostics.SourceLocation Nowhere = new("", 0, 0);

    /// <summary>
    /// Whether a source on an OUTER APPLY's right side is never null-extended: when it is the whole right side and always
    /// has a row (observed: An_ungrouped_aggregate_applied_gives_one_row_for_every_outer_row,
    /// A_for_json_subquery_applied_without_from_is_one_row_whose_json_is_null_when_there_are_no_rows,
    /// A_values_list_gives_one_row_for_each_of_its_rows_for_every_outer_row). A relation joined inside the right side is
    /// not enough: the join may still leave the side without a row.
    /// </summary>
    private static bool AlwaysApplied(BoundSource source, ReadScope scope) =>
        source.Optional?.Kind == OptionalSideKind.WholeApplied && scope.Of(source).AtLeastOne;

    // ---- VALUES ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A VALUES list, <c>(VALUES (a, b), (c, d)) AS v (x, y)</c>: as many rows as it lists, each time it is read (for an APPLY's
    /// right side, for each outer row), so always at least one. A column holds its rows' values, null when any may be. Its
    /// type is the one of highest precedence among its rows', to which every value is converted (observed:
    /// A_values_column_takes_the_type_of_highest_precedence_among_its_rows): rows whose values all have the same shape keep
    /// it, whose claims then hold of each value; a column of string literals is the longest (<see cref="Literals"/>); rows of
    /// different types are typed as a set operation's column is (<see cref="SetColumn"/>). JSON passes through as JSON
    /// (observed: Json_passes_through_a_values_list_as_json).
    /// </summary>
    /// <param name="symbol">The list, bound.</param>
    /// <param name="scope">The scope its rows are read in: no FROM of their own, an enclosing query's or an APPLY's left input's beyond.</param>
    private LocalRelationValues ValuesList(ValuesTable symbol, ReadScope scope)
    {
        var table = symbol.Syntax;
        var at = At(table);
        var name = symbol.Name.Length > 0 ? symbol.Name : "(VALUES)";
        LocalRelationValues Fails(string detail) => LocalRelationValues.Unknown(Unsupported(IrUnknownReason.UnsupportedQuery, $"{name}: {detail}", at));

        // SQL Server requires a name for every column (8155) and the same number of values in every row (10709).
        if (table.Columns.Count == 0 || table.RowValues.Any(r => r.ColumnValues.Count != table.Columns.Count))
            return Fails("a VALUES list needs a name for each column and as many values in each row.");
        var names = table.Columns.Select(c => c.Value).ToList();
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count)
            return Fails("its column names are not distinct.");

        // Its rows are read with no FROM of their own: an enclosing query's columns, or an APPLY's left input's.
        var rows = table.RowValues.Select(r => r.ColumnValues.Select(v => Expression(v, scope)).ToList()).ToList();
        var values = new List<Value>();
        for (var i = 0; i < names.Count; i++)
        {
            var column = rows.Select(r => r[i]).ToList();
            if (Literals([.. table.RowValues.Select(r => r.ColumnValues[i])], at) is { } literals)
            {
                values.Add(literals);
                continue;
            }

            if (column.FirstOrDefault(v => v.Shape is UnknownShape) is { } unknown)
            {
                values.Add(unknown);
                continue;
            }

            // Rows of one type hold what any of them holds (the knowledge join). The server folds the column to its literal
            // only when there is one row: of several, even the same literal, it counts the type's digits (observed:
            // A_values_lists_int_column_is_a_constant_in_one_row_only).
            var first = column[0];
            if (column.All(v => v.IsLiveJson == first.IsLiveJson && v.Shape == first.Shape))
            {
                values.Add(column.Count == 1 ? first : column.Skip(1).Aggregate(first, (a, b) => Value.Join(a, b, at, $"{name}.{names[i]}")) with { Digits = IntDigits.OfType });
                continue;
            }

            // Rows of different types are typed as a UNION ALL of the rows is, by the one rule for combining types, each value
            // converted to the type of highest precedence and a NULL adding only its null (observed:
            // A_values_column_takes_the_type_of_highest_precedence_among_its_rows, and every pair and triple of
            // SetTypeGridTests' grid in its values form). Live JSON beside another kind is not: whether it comes out as JSON
            // or text depends on how it was made.
            values.Add(column.Any(v => v.IsLiveJson)
                ? Value.Unknown(Unsupported(IrUnknownReason.UnsupportedExpression, $"{name}.{names[i]}: JSON beside values of another kind is not modelled.", at))
                : SetColumn(
                    [.. column.Select((v, r) => new RowColumn(names[i], v, At(table.RowValues[r].ColumnValues[i]), Expression: table.RowValues[r].ColumnValues[i]))],
                    UnionAll(column.Count),
                    at,
                    $"{name}.{names[i]}"));
        }

        return new LocalRelationValues([.. names], [.. values], null)
        {
            Rows = rows.Count == 1 ? IrCardinality.One : IrCardinality.Many,
            AtLeastOne = true,
        };
    }

    /// <summary>
    /// A VALUES column whose every row is a string literal (the label of the unpivot idiom): never null, of type varchar of
    /// the longest literal, at least 1, or nvarchar when any is <c>N'…'</c> (observed:
    /// A_values_column_of_string_literals_is_a_string_of_the_longest). A varchar literal's length counts bytes of the
    /// database's code page, which the project does not show, so one with a character beyond ASCII is left out, and so is one
    /// holding a SQLCMD variable, whose text is not its value. Null when the column is anything else. Only VALUES reads string
    /// literals so far.
    /// </summary>
    private static Value? Literals(List<ScalarExpression> column, Diagnostics.SourceLocation at)
    {
        var literals = column.Select(Strip).OfType<StringLiteral>().ToList();
        if (literals.Count != column.Count || literals.Any(l => l.Collation is not null || SqlcmdVariableReference.IsIn(l) || (!l.IsNational && l.Value.Any(c => c > '\u007f'))))
            return null;
        var national = literals.Any(l => l.IsNational);
        var length = Math.Max(1, literals.Max(l => l.Value.Length));
        return new Value(SqlTypes.ToConvertedShape(ScalarType.CharacterString(national, varying: true, length), at), []);
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
    public LocalRelationValues ReadView(View view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (view.Redefinition is not null)
            throw new ArgumentException("The view has no single definition.", nameof(view));
        var at = view.Span.Start;
        LocalRelationValues Fails(string detail) => LocalRelationValues.Unknown(Unsupported(IrUnknownReason.UnsupportedQuery, $"{view.Name}: {detail}", at));

        // A view whose text the source lacks: its columns are known by name, their values not at all. An encrypted one's text is
        // withheld by the server; a system view's columns are untyped in master.dacpac (SystemViews).
        if (view.Query is not { } select)
        {
            return LocalRelationValues.Unknown(view.IsEncrypted
                ? Unsupported(IrUnknownReason.EncryptedDefinition, $"{view.Name} is encrypted, and the source holds a placeholder, not its text, so what its columns hold is unknown.", at)
                : Unsupported(IrUnknownReason.UnsupportedSource, $"{view.Name} is a system view, whose columns master.dacpac lists without their types.", at));
        }

        // Its stars are expanded, as SQL Server bound them when it created the view.
        return WithCtes(
            select.WithCtesAndXmlNamespaces,
            ReadScope.Statement(NameEnvironment.Empty, FlowState.Start([])),
            scope =>
            {
                if (select.QueryExpression is not { } query || ForOf(query) is not null)
                    return Fails("a view whose query is FOR XML or FOR JSON is not modelled yet.");
                var bound = binder.Bind(query, null, scope.Names);
                var row = Query(bound, scope);
                return Holds(bound.Listed(view.Columns), row) is { } relation
                    ? relation with { Rows = row.Rows, AtLeastOne = row.Rows == IrCardinality.One, Keys = Keys(query, row, scope) }
                    : Unlisted(view.Name.ToString(), row, at);
            },
            LocalRelationValues.Unknown);
    }

    /// <summary>A set operation keeps no key (UNION ALL repeats one); a query specification's are <see cref="ViewKeys"/>.</summary>
    private EquatableArray<RelationKey> Keys(QueryExpression query, Row row, ReadScope scope) => query is QuerySpecification specification ? [.. ViewKeys(specification, row, scope)] : [];

    /// <summary>
    /// The keys a view has: those of its one table that it selects whole, unchanged. A view that reads one table, without a
    /// join, APPLY or GROUP BY, returns some of the table's rows, each once (observed:
    /// A_view_over_one_table_returns_each_of_its_rows_at_most_once), so no two of its rows share a key the table enforces.
    /// WHERE, TOP and DISTINCT only drop rows. GROUP BY is left out, though grouping by a whole key keeps it, since ROLLUP and
    /// its kind add rows that would share it; a join is left out, though one that matches at most one row keeps the other
    /// side's keys, because proving that needs the joined side's key fixed by the ON, and views rarely need it.
    /// </summary>
    private List<RelationKey> ViewKeys(QuerySpecification query, Row row, ReadScope scope)
    {
        if (ReadUncommitted(scope) || query.GroupByClause is not null
            || row.Sources is not [{ Target: TableTarget { Table: var table }, Optional: null, History: false, Uncommitted: false } source])
        {
            return [];
        }

        var keys = new List<RelationKey>();
        foreach (var key in SchemaRules.EnforcedKeys(table))
        {
            var ordinals = new List<int>();
            var types = new List<BuiltInType>();
            foreach (var name in key.Columns)
            {
                var ordinal = row.Columns.ToList().FindIndex(c => c.Origin is { } origin && ReferenceEquals(origin.Source, source)
                    && origin.Column.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (ordinal < 0 || SchemaRules.ResolveType(model, table.Column(name)?.Type)?.Type is not { } type)
                    break;
                ordinals.Add(ordinal);
                types.Add(type);
            }

            if (ordinals.Count == key.Columns.Count)
                keys.Add(new RelationKey([.. ordinals], [.. types]));
        }

        return keys;
    }
}
