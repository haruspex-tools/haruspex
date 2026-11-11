using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;
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

/// <summary>
/// Whether the triggers a write fires may return result sets (slice 6d). A trigger's result sets arrive in the caller's
/// stream where the statement is, even when no row is affected, and SQL Server allows them unless the server's
/// <c>disallow results from triggers</c> option is on, which it is not by default (observed in <c>DmlObservationTests</c>;
/// README.md in this folder, "Triggers"). So a statement that fires a trigger of the project that may return results
/// makes the procedure's results unknown, and a write to a table the project does not define gets a warning, once per
/// table, since a trigger there would not be seen.
/// </summary>
/// <remarks>
/// <para>
/// Which triggers fire is followed transitively, each step observed: the triggers on the table for the statement's events
/// (an <c>INSTEAD OF</c> one included); for a view, its <c>INSTEAD OF</c> triggers, and for the events none covers, the
/// tables it reads; for a delete or update, the triggers of the tables whose foreign keys cascade from it; and each
/// trigger's own writes and calls. Every reachable trigger is checked, a superset of those that fire, which claims less.
/// </para>
/// <para>
/// A trigger may return results unless every statement in it is known not to: a list kept short on purpose. A
/// <c>SELECT</c> that returns rows, an <c>OUTPUT</c> without <c>INTO</c>, a <c>FETCH</c> without <c>INTO</c>, a call of
/// anything but a procedure of the project (which is read the same way), and any statement not on the list count.
/// </para>
/// </remarks>
internal sealed class DmlTriggers(DatabaseModel model, ICollection<Diagnostic>? warnings)
{
    private readonly HashSet<string> warned = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Why a write fires a trigger that may return result sets, or null when none of those it may fire does. Warns, once per
    /// name, of each object written that the project does not define.
    /// </summary>
    /// <param name="target">What the write is aimed at.</param>
    /// <param name="binder">The binder of the body the write is in, which resolves its names.</param>
    /// <param name="events">The statements the write is, for triggers: a MERGE is each of its actions.</param>
    /// <param name="at">Where the write is.</param>
    public UnknownShape? Check(DmlTarget target, Binder binder, DmlEvents events, SourceLocation at)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var written in target.Objects)
        {
            var resolved = binder.ResolveWritten(written);
            if (resolved.Count == 0)
                Warn(FragmentText.OneLine(written), at);
            foreach (var name in resolved)
            {
                if (Reaches(name, events, at, visited) is { } why)
                    return new UnknownShape(IrUnknownReasons.TriggerResults, $"The write at {at} {why}", at);
            }
        }

        return null;
    }

    private void Warn(string name, SourceLocation at)
    {
        if (warnings is not null && warned.Add(name))
        {
            warnings.Add(new Diagnostic(
                $"{name} is written here but is not defined in the project, so a trigger on it, which could return result sets of its own, cannot be seen. SQL Server allows results from triggers unless the server's 'disallow results from triggers' option is on, which it is not by default.",
                at,
                DiagnosticSeverity.Warning));
        }
    }

    /// <summary>How writing <paramref name="name"/> fires a trigger that may return results, as words; null when it fires none.</summary>
    private string? Reaches(ObjectName name, DmlEvents events, SourceLocation at, HashSet<string> visited)
    {
        // A write through a synonym writes its object (observed: A_write_through_a_synonym_fires_the_triggers_of_its_table).
        if (model.FindNamedObject(name) is { Kind: NamedObjectKind.Synonym } synonym)
        {
            if (!visited.Add($"synonym {name}"))
                return null;
            var objects = synonym.SynonymFor is { } written ? new Binder(model, at.File).ResolveWritten(written) : [];
            if (objects.Count == 0)
                Warn($"{name} (a synonym for {(synonym.SynonymFor is { } far ? FragmentText.OneLine(far) : "an object not known")})", at);
            foreach (var target in objects)
            {
                if (Reaches(target, events, at, visited) is { } why)
                    return $"writes {target} through the synonym {name}, and {why}";
            }

            return null;
        }

        var view = model.FindNamedObject(name) is { Kind: NamedObjectKind.View } named ? named : null;
        var table = model.FindTable(name) is not null || model.FindTableRedefinition(name) is not null;
        if (view is null && !table)
        {
            Warn(name.ToString(), at);
            return null;
        }

        if (!visited.Add($"{name}|{(int)events}"))
            return null;

        var remaining = events;
        foreach (var trigger in model.TriggersOn(name).Where(t => (t.Events & events) != 0))
        {
            if (Returns(trigger, visited) is { } why)
                return $"fires the trigger {trigger.Name} on {name} ({trigger.At}), which {why}";

            // On a view, INSTEAD OF runs in place of the statement, so the tables underneath are written only by what it does.
            if (view is not null && trigger.InsteadOf)
                remaining &= ~trigger.Events;
        }

        if (view is not null && remaining != DmlEvents.None)
        {
            // Observed: Dml_through_a_view_or_a_cte_fires_the_triggers_of_the_table_underneath.
            if (view.View is not { } definition)
                return $"writes the view {name}, which is defined more than once, so the tables it writes underneath are not known";
            var underneath = new DmlTarget.NamedTables();
            definition.SelectStatement.Accept(underneath);
            var binder = new Binder(model, at.File);
            var own = definition.SelectStatement.WithCtesAndXmlNamespaces?.CommonTableExpressions.Select(c => c.ExpressionName.Value).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
            foreach (var read in underneath.Found.Where(n => n.SchemaIdentifier is not null || !own.Contains(n.BaseIdentifier.Value)))
            {
                if (binder.ResolveWritten(read).Count == 0)
                    Warn(FragmentText.OneLine(read), at);
                foreach (var below in binder.ResolveWritten(read))
                {
                    if (Reaches(below, remaining, at, visited) is { } why)
                        return $"writes the view {name}, over {below}, and {why}";
                }
            }
        }

        // Observed: A_cascading_delete_fires_the_triggers_of_the_referencing_table. ON DELETE SET NULL and ON UPDATE
        // actions update the referencing rows, CASCADE deletes or updates them: both events are counted for each.
        if (table && (events & (DmlEvents.Delete | DmlEvents.Update)) != 0)
        {
            foreach (var key in model.CascadingKeysTo(name))
            {
                if (Reaches(key.Referencing, DmlEvents.Delete | DmlEvents.Update, at, visited) is { } why)
                    return $"changes {key.Referencing} through the foreign key at {key.At}, and {why}";
            }
        }

        return null;
    }

    /// <summary>Why a trigger may return result sets, as words; null when every statement in it is known to return none.</summary>
    private string? Returns(Trigger trigger, HashSet<string> visited) => trigger.Body is { } body
        ? Statements(body, new Binder(model, trigger.At.File), trigger.At, visited)
        : "is a CLR trigger, whose code is not in the project";

    private string? Statements(TSqlFragment body, Binder binder, SourceLocation where, HashSet<string> visited)
    {
        var found = new StatementFinder();
        body.Accept(found);
        foreach (var statement in found.Statements)
        {
            var at = new SourceLocation(where.File, statement.StartLine, statement.StartColumn);
            if (Statement(statement, found.CursorQueries, binder, at, visited) is { } why)
                return why;
        }

        return null;
    }

    private string? Statement(TSqlStatement statement, HashSet<SelectStatement> cursorQueries, Binder binder, SourceLocation at, HashSet<string> visited)
    {
        return statement switch
        {
            SelectStatement select when cursorQueries.Contains(select) || select.Into is not null
                || (select.QueryExpression is QuerySpecification { SelectElements: var elements } && elements.All(e => e is SelectSetVariable)) => null,
            SelectStatement => $"returns a result set at {at}",
            FetchCursorStatement { IntoVariables.Count: 0 } => $"returns a row with FETCH at {at}",
            FetchCursorStatement => null,
            InsertStatement insert => Write(insert.InsertSpecification, null, insert.WithCtesAndXmlNamespaces, DmlEvents.Insert, binder, at, visited)
                ?? (insert.InsertSpecification.InsertSource is ExecuteInsertSource source ? Call(source.Execute, binder, at, visited) : null),
            UpdateStatement update => Write(update.UpdateSpecification, update.UpdateSpecification.FromClause, update.WithCtesAndXmlNamespaces, DmlEvents.Update, binder, at, visited),
            DeleteStatement delete => Write(delete.DeleteSpecification, delete.DeleteSpecification.FromClause, delete.WithCtesAndXmlNamespaces, DmlEvents.Delete, binder, at, visited),
            MergeStatement merge => Write(merge.MergeSpecification, null, merge.WithCtesAndXmlNamespaces, MergeEvents(merge.MergeSpecification), binder, at, visited),
            ExecuteStatement execute => Call(execute.ExecuteSpecification, binder, at, visited),
            BeginEndBlockStatement or IfStatement or WhileStatement or TryCatchStatement
                or DeclareVariableStatement or DeclareTableVariableStatement or SetVariableStatement or PredicateSetStatement
                or PrintStatement or RaiseErrorStatement or ThrowStatement or ReturnStatement or BreakStatement or ContinueStatement
                or BeginTransactionStatement or CommitTransactionStatement or RollbackTransactionStatement or SaveTransactionStatement
                or CreateTableStatement or DropTableStatement or AlterTableStatement or CreateIndexStatement or TruncateTableStatement
                or DeclareCursorStatement or OpenCursorStatement or CloseCursorStatement or DeallocateCursorStatement
                or GoToStatement or LabelStatement or SetRowCountStatement => null,
            _ => $"runs {statement.ScriptTokenStream[statement.FirstTokenIndex].Text.ToUpperInvariant()} at {at}, which is not known to return no result set",
        };
    }

    private string? Write(DataModificationSpecification spec, FromClause? from, WithCtesAndXmlNamespaces? with, DmlEvents events, Binder binder, SourceLocation at, HashSet<string> visited)
    {
        if (spec.OutputClause is not null)
            return $"returns the rows of its OUTPUT at {at}";
        var target = DmlTarget.Of(spec.Target, from, with);
        if (target.Unmodelled is { } unmodelled)
            return $"writes at {at} what is not followed: {unmodelled}";
        foreach (var written in target.Objects)
        {
            var resolved = binder.ResolveWritten(written);
            if (resolved.Count == 0)
                Warn(FragmentText.OneLine(written), at);
            foreach (var name in resolved)
            {
                if (Reaches(name, events, at, visited) is { } why)
                    return $"writes at {at}, which {why}";
            }
        }

        return null;
    }

    private string? Call(ExecuteSpecification execute, Binder binder, SourceLocation at, HashSet<string> visited)
    {
        switch (binder.BindCall(execute))
        {
            case ProcedureCall { Procedure: var callee }:
                if (!visited.Add($"procedure {callee.Name}"))
                    return null;
                if (callee.Body.StatementList is not { } body || callee.Body.MethodSpecifier is not null)
                    return $"calls {callee.Name} at {at}, a CLR procedure";
                return Statements(body, new Binder(model, callee.File), callee.At, visited) is { } why ? $"calls {callee.Name} at {at}, which {why}" : null;
            case UnknowableCall unknowable:
                return $"calls what is not followed at {at}: {unknowable.Detail}";
            default:
                return $"calls what is not followed at {at}";
        }
    }

    /// <summary>The statements a MERGE is, for triggers: one per kind of action it has (observed: Merge_fires_the_trigger_of_each_action_it_has).</summary>
    public static DmlEvents MergeEvents(MergeSpecification merge)
    {
        var events = DmlEvents.None;
        foreach (var clause in merge.ActionClauses)
        {
            events |= clause.Action switch
            {
                InsertMergeAction => DmlEvents.Insert,
                UpdateMergeAction => DmlEvents.Update,
                DeleteMergeAction => DmlEvents.Delete,
                _ => DmlEvents.Insert | DmlEvents.Update | DmlEvents.Delete,
            };
        }

        return events;
    }

    /// <summary>Every statement in a body, at any depth, and the queries that only define cursors.</summary>
    private sealed class StatementFinder : TSqlFragmentVisitor
    {
        public List<TSqlStatement> Statements { get; } = [];

        public HashSet<SelectStatement> CursorQueries { get; } = new(ReferenceEqualityComparer.Instance);

        public override void Visit(TSqlStatement node) => Statements.Add(node);

        public override void Visit(CursorDefinition node)
        {
            if (node.Select is not null)
                CursorQueries.Add(node.Select);
        }
    }
}
