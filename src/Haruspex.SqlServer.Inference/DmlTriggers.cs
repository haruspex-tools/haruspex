using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;
using Haruspex.SqlServer.Syntax;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// Whether the triggers a write fires may return result sets. A trigger's result sets arrive in the caller's
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
internal sealed class DmlTriggers(DatabaseModel model, List<Diagnostic> warnings)
{
    private readonly HashSet<string> warned = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Why a write fires a trigger that may return result sets, or null when none of those it may fire does, with every trigger it
    /// may fire, at any depth, when none does. Warns, once per name, of each object written that the project does not define.
    /// </summary>
    /// <param name="target">What the write is aimed at.</param>
    /// <param name="binder">The binder of the body the write is in, which resolves its names.</param>
    /// <param name="events">The statements the write is, for triggers: a MERGE is each of its actions.</param>
    /// <param name="at">Where the write is.</param>
    public (UnknownShape? Why, IReadOnlyList<Trigger> Reached) Check(DmlTarget target, Binder binder, DmlEvents events, SourceLocation at)
    {
        var visited = new HashSet<Visit>();
        foreach (var written in target.Objects)
        {
            var resolved = binder.ResolveWritten(written);
            if (resolved.Count == 0)
                Warn(written, at);
            foreach (var name in resolved)
            {
                if (Reaches(name, events, binder, at, visited) is { } why)
                    return (new UnknownShape(IrUnknownReason.TriggerResults, $"The write at {at} {why}", at), []);
            }
        }

        return (null, [.. visited.OfType<TriggerVisit>().Select(v => v.Trigger)]);
    }

    /// <summary>Warns of a name written that resolves to nothing, as written, by its canonical subject (<see cref="Subject(ObjectName)"/>).</summary>
    private void Warn(SchemaObjectName written, SourceLocation at) =>
        Warn(FragmentText.OneLine(written), string.Join('.', written.Identifiers.Select(i => i.Value)), at);

    /// <summary>
    /// What a config file acknowledges a warning about <paramref name="name"/> by: its parts joined by dots, without brackets, as
    /// <c>audit.log</c>, so that <c>[audit].[log]</c> and <c>audit.log</c> are one subject; the CLI compares subjects ignoring
    /// case, as SQL Server compares names.
    /// </summary>
    private static string Subject(ObjectName name) => name.Database is { } database ? $"{database}.{name.Schema}.{name.Name}" : $"{name.Schema}.{name.Name}";

    /// <summary>Warns, once for each subject, of an object written that the project doesn't define: <paramref name="name"/> as the message shows it.</summary>
    private void Warn(string name, string subject, SourceLocation at)
    {
        if (warned.Add(subject))
        {
            warnings.Add(InferenceCodes.UndefinedWriteTarget.At(
                at,
                $"{name} is written here but is not defined in the project, so a trigger on it, which could return result sets of its own, cannot be seen. SQL Server allows results from triggers unless the server's 'disallow results from triggers' option is on, which it is not by default.",
                subject));
        }
    }

    /// <summary>How writing <paramref name="name"/> fires a trigger that may return results, as words; null when it fires none.</summary>
    /// <param name="binder">The binder of the body the write is in, which resolves the names written underneath too.</param>
    private string? Reaches(ObjectName name, DmlEvents events, Binder binder, SourceLocation at, HashSet<Visit> visited)
    {
        // A write through a synonym writes its object (observed: A_write_through_a_synonym_fires_the_triggers_of_its_table).
        if (model.FindReadable(name) is Synonym synonym)
        {
            if (!visited.Add(new SynonymVisit(name)))
                return null;
            var objects = binder.ResolveWritten(synonym);
            if (objects.Count == 0)
                Warn($"{name} (a synonym for {synonym.For})", Subject(name), at);
            foreach (var target in objects)
            {
                if (Reaches(target, events, binder, at, visited) is { } why)
                    return $"writes {target} through the synonym {name}, and {why}";
            }

            return null;
        }

        var view = model.FindReadable(name) as View;
        var table = model.FindTable(name) is not null || model.FindTableRedefinition(name) is not null;
        if (view is null && !table)
        {
            Warn(name.ToString(), Subject(name), at);
            return null;
        }

        if (!visited.Add(new WriteVisit(name, events)))
            return null;

        var remaining = events;
        foreach (var trigger in model.TriggersOn(name).Where(t => (t.Events & events) != 0))
        {
            visited.Add(new TriggerVisit(trigger));
            if (Returns(trigger, visited) is { } why)
                return $"fires the trigger {trigger.Name} on {name} ({trigger.Span}), which {why}";

            // On a view, INSTEAD OF runs in place of the statement, so the tables underneath are written only by what it does.
            if (view is not null && trigger.InsteadOf)
                remaining &= ~trigger.Events;
        }

        if (view is not null && remaining != DmlEvents.None)
        {
            // Observed: Dml_through_a_view_or_a_cte_fires_the_triggers_of_the_table_underneath.
            if (view.Redefinition is not null)
                return $"writes the view {name}, which is defined more than once, so the tables it writes underneath are not known";
            if (view.Query is null)
                return $"writes the view {name}, which is encrypted, so the tables it writes underneath are not known";
            foreach (var read in DmlTarget.Underneath(view.Query))
            {
                if (binder.ResolveWritten(read).Count == 0)
                    Warn(read, at);
                foreach (var below in binder.ResolveWritten(read))
                {
                    if (Reaches(below, remaining, binder, at, visited) is { } why)
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
                if (Reaches(key.Referencing, DmlEvents.Delete | DmlEvents.Update, binder, at, visited) is { } why)
                    return $"changes {key.Referencing} through the foreign key at {key.At}, and {why}";
            }
        }

        return null;
    }

    /// <summary>Why a trigger may return result sets, as words; null when every statement in it is known to return none.</summary>
    private string? Returns(Trigger trigger, HashSet<Visit> visited) => trigger.Body switch
    {
        SqlBody body => Statements(body.Statements, new Binder(model, trigger.Span, SchemaSearch.Within(trigger)), trigger.Span, visited),
        EncryptedBody => "is encrypted, and the source holds a placeholder, not its text",
        ClrBody or NoBody => "is a CLR trigger, whose code is not in the project",
    };

    private string? Statements(TSqlFragment body, Binder binder, SourceSpan where, HashSet<Visit> visited)
    {
        var found = new StatementFinder();
        body.Accept(found);
        foreach (var statement in found.Statements)
        {
            var at = where.At(statement);
            if (Statement(statement, found.CursorQueries, binder, at, visited) is { } why)
                return why;
        }

        return null;
    }

    private string? Statement(TSqlStatement statement, HashSet<SelectStatement> cursorQueries, Binder binder, SourceLocation at, HashSet<Visit> visited)
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
                or CreateTableStatement or DropTableStatement or AlterTableStatement or CreateIndexStatement or AlterIndexStatement or DropIndexStatement
                or TruncateTableStatement
                or DeclareCursorStatement or OpenCursorStatement or CloseCursorStatement or DeallocateCursorStatement
                or GoToStatement or LabelStatement or SetRowCountStatement => null,
            _ => $"runs {statement.ScriptTokenStream[statement.FirstTokenIndex].Text.ToUpperInvariant()} at {at}, which is not known to return no result set",
        };
    }

    private string? Write(DataModificationSpecification spec, FromClause? from, WithCtesAndXmlNamespaces? with, DmlEvents events, Binder binder, SourceLocation at, HashSet<Visit> visited)
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
                Warn(written, at);
            foreach (var name in resolved)
            {
                if (Reaches(name, events, binder, at, visited) is { } why)
                    return $"writes at {at}, which {why}";
            }
        }

        return null;
    }

    private string? Call(ExecuteSpecification execute, Binder binder, SourceLocation at, HashSet<Visit> visited)
    {
        switch (binder.BindCall(execute))
        {
            case ProcedureCall { Procedure: var callee }:
                if (!visited.Add(new CallVisit(callee.Name)))
                    return null;
                return callee.Body switch
                {
                    SqlBody body => Statements(body.Statements, new Binder(model, callee.Span, SchemaSearch.Within(callee)), callee.Span, visited) is { } why ? $"calls {callee.Name} at {at}, which {why}" : null,
                    EncryptedBody => $"calls {callee.Name} at {at}, which is encrypted, and the source holds a placeholder, not its text",
                    ClrBody or NoBody => $"calls {callee.Name} at {at}, a CLR procedure",
                };
            case SystemProcedureCall:
                return null; // writes no table of the database (SystemProcedures)
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

    /// <summary>
    /// What the walk of the triggers a write may fire has already followed, so that a cycle (a trigger writing its own table,
    /// a procedure calling itself) is followed once. Names compare as SQL Server compares them (<see cref="ObjectName"/>).
    /// </summary>
    private abstract record Visit
    {
        private protected Visit()
        {
        }
    }

    /// <summary>A synonym written through, which writes its object.</summary>
    private sealed record SynonymVisit(ObjectName Synonym) : Visit;

    /// <summary>A table or view written as these statements, whose triggers for them were followed.</summary>
    private sealed record WriteVisit(ObjectName Target, DmlEvents Events) : Visit;

    /// <summary>A procedure called from a trigger's body, or from one it calls, whose statements were followed.</summary>
    private sealed record CallVisit(ObjectName Procedure) : Visit;

    /// <summary>A trigger reached, which may fire.</summary>
    private sealed record TriggerVisit(Trigger Trigger) : Visit;
}
