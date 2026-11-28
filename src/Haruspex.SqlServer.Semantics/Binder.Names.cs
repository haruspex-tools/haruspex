using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Syntax;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Semantics;

// Schema names written without a schema, and what an EXEC runs.
public sealed partial class Binder
{
    /// <summary>
    /// The local temporary table a name written as a table means: its last part, when that starts with one <c>#</c>; null for
    /// any other. SQL Server ignores every other part of such a name, whatever schema, database or server it names
    /// (<c>dbo.#t</c>, <c>tempdb..#t</c>): a statement that reads or writes it takes up to four parts (<c>srv.tempdb.dbo.#t</c>),
    /// and one that makes or drops it up to three, refusing a fourth with Msg 117 (observed:
    /// A_temporary_table_named_with_a_schema_database_or_server_is_the_sessions_own,
    /// A_temporary_table_is_made_or_dropped_with_three_parts_at_most), so a write through such a name
    /// is a write to the table, which the walk must see. A global one (<c>##t</c>) is shared with every session, which may
    /// change it at any time, so it is read as a permanent table is. The walk keeps what each holds by this name, and a body
    /// can create a given <c>#t</c> once (observed: A_procedure_cannot_create_a_temporary_table_twice_even_after_dropping_it).
    /// </summary>
    /// <param name="name">The name as a statement writes it: <c>CREATE TABLE</c>, <c>DROP TABLE</c>, <c>SELECT … INTO</c>, a FROM.</param>
    public static LocalName? Temporary(SchemaObjectName name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name is { BaseIdentifier.Value: var own } && own.StartsWith('#') && !own.StartsWith("##", StringComparison.Ordinal)
            ? new LocalName(own)
            : null;
    }

    /// <summary>
    /// The object a name means when it is written with its schema, in this database: <c>dbo.box</c>. Null for a name without
    /// its schema, whose meaning depends on where it is written and who runs it, and for one in another database or on a linked
    /// server. For a reference whose resolution without a schema isn't modelled, such as <c>WITH RESULT SETS (AS OBJECT …)</c>.
    /// </summary>
    /// <param name="name">The name as written.</param>
    public static ObjectName? Qualified(SchemaObjectName name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name is { ServerIdentifier: null, DatabaseIdentifier: null, SchemaIdentifier.Value: var schema, BaseIdentifier.Value: var own }
            ? new ObjectName(schema, own)
            : null;
    }

    /// <summary>
    /// The objects a name written as the target of a write may mean, by the rule <see cref="Bind"/> resolves table names
    /// with: a schema-qualified name means itself; an unqualified one, what <see cref="SchemaNames.Resolve"/> finds among the
    /// tables, views and synonyms (every one of them where it finds several, since which is written depends on the caller's
    /// default schema), and <c>dbo</c>'s when it finds none. Empty for a name in another database or on a linked server, which
    /// the project cannot show.
    /// </summary>
    /// <param name="name">The name as written; a temporary table's or a body's own relation's is not asked about.</param>
    public IReadOnlyList<ObjectName> ResolveWritten(SchemaObjectName name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return ResolveWritten(name.ServerIdentifier is not null || name.DatabaseIdentifier is not null, name.SchemaIdentifier?.Value, name.BaseIdentifier.Value, search);
    }

    /// <summary>
    /// The objects a synonym's target may mean, by the rule <see cref="ResolveWritten(SchemaObjectName)"/> gives, except that a
    /// target without a schema is looked for in the synonym's schema and then in <c>dbo</c>, whoever reads it (see
    /// <see cref="SchemaSearch"/>).
    /// </summary>
    /// <param name="synonym">The synonym.</param>
    public IReadOnlyList<ObjectName> ResolveWritten(Synonym synonym)
    {
        ArgumentNullException.ThrowIfNull(synonym);
        var target = synonym.For;
        return ResolveWritten(target.Server is not null || target.Database is not null, target.Schema, target.Name, SchemaSearch.Within(synonym));
    }

    private List<ObjectName> ResolveWritten(bool elsewhere, string? schema, string name, SchemaSearch searched)
    {
        if (elsewhere)
            return [];
        if (schema is not null)
            return [new ObjectName(schema, name)];
        // By the caller's rule, a function is read, never written: every table, view or synonym of the name may be what is
        // written. In a module's definition the name means its schema's object, or dbo's, whatever it is, a function included.
        var objects = searched.Own is null ? Readable().Where(o => o is not Function) : Readable();
        return SchemaNames.Resolve(objects, name, searched) switch
        {
            OneObject one => [one.Named.Name],
            SeveralObjects several => [.. several.Candidates.Select(c => c.Name)],
            NoObject none => [none.InDbo],
        };
    }

    /// <summary>
    /// The sequence <c>NEXT VALUE FOR</c> names: by its schema when it names one, and otherwise as a table's name is found from
    /// where it is written, in a module's schema and then <c>dbo</c> whoever calls (observed:
    /// An_unqualified_sequence_in_a_procedure_is_looked_for_in_the_procedures_schema_then_dbo). Null for a name the model has no
    /// sequence of, one several schemas share where the caller decides, and one in another database or on a linked server.
    /// </summary>
    /// <param name="name">The name as written.</param>
    public Sequence? ResolveSequence(SchemaObjectName name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.ServerIdentifier is not null || name.DatabaseIdentifier is not null)
            return null;
        if (name.SchemaIdentifier is not null)
            return Model.FindSequence(SyntaxReader.Name(name));
        return SchemaNames.Resolve(Model.Sequences, name.BaseIdentifier.Value, search) is OneObject { Named: Sequence one } ? one : null;
    }

    /// <summary>Every object a FROM clause could read: the tables, and the views, synonyms and functions.</summary>
    private IEnumerable<SchemaObject> Readable() => Model.Tables.Cast<SchemaObject>().Concat(Model.Readable);

    // ---- EXEC ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Binds what an <c>EXEC</c> runs: a procedure of the project, or why its results cannot be known from the project. An
    /// unqualified name resolves by the caller's rule even inside a module (<see cref="SchemaSearch.Caller"/>): to the one
    /// procedure of that name, if exactly one schema has it, since SQL Server looks in the caller's default schema and then in
    /// <c>dbo</c>.
    /// </summary>
    /// <param name="execute">The <c>EXEC</c>'s specification: of a statement, or of <c>INSERT … EXEC</c>.</param>
    public CallTarget BindCall(ExecuteSpecification execute)
    {
        ArgumentNullException.ThrowIfNull(execute);
        var at = At(execute);
        var text = FragmentText.OneLine(execute);
        UnknowableCall Unknowable(UnknowableCallKind kind, string detail, ObjectName? name = null) => new(kind, name, detail, at);

        if (execute.LinkedServer is not null)
            return Unknowable(UnknowableCallKind.MissingProcedure, $"{text} runs on the linked server {execute.LinkedServer.Value}, whose procedures are not in the project.");

        return execute.ExecutableEntity switch
        {
            ExecutableStringList => Unknowable(UnknowableCallKind.DynamicSql, $"{text} runs dynamic SQL, a string built when the procedure runs, so its result sets are not known."),
            ExecutableProcedureReference { ProcedureReference.ProcedureVariable: { } variable } => Unknowable(UnknowableCallKind.ProcedureVariable, $"{text} runs the procedure named in {variable.Name}, which is chosen when it runs."),
            ExecutableProcedureReference { AdHocDataSource: not null } => Unknowable(UnknowableCallKind.MissingProcedure, $"{text} runs a procedure through OPENDATASOURCE, which is not in the project."),
            ExecutableProcedureReference { ProcedureReference.ProcedureReference.Name: { } name } => BindProcedure(name, text, at),
            _ => Unknowable(UnknowableCallKind.MissingProcedure, $"{text} is not a form of EXEC that is modelled."),
        };
    }

    private CallTarget BindProcedure(SchemaObjectName name, string text, SourceLocation at)
    {
        var own = name.BaseIdentifier.Value;

        // sp_executesql is dynamic SQL, under any qualification. Any other sp_ or xp_ name may be one of SQL Server's own
        // procedures, which it runs instead of the database's whatever schema is written (observed), and which ones it has is
        // not known here; so even a procedure of the project with such a name is not followed.
        if (own.Equals("sp_executesql", StringComparison.OrdinalIgnoreCase))
            return new UnknowableCall(UnknowableCallKind.DynamicSql, null, $"{text} runs dynamic SQL, a string built when the procedure runs, so its result sets are not known.", at);
        if (own.StartsWith("sp_", StringComparison.OrdinalIgnoreCase) || own.StartsWith("xp_", StringComparison.OrdinalIgnoreCase))
        {
            // One the catalogue lists is SQL Server's own whatever the schema written, as above.
            if (SystemProcedures.Find(own) is { } known)
                return new SystemProcedureCall(known, at);

            var system = new ObjectName(name.SchemaIdentifier?.Value ?? "sys", own);
            var also = Model.FindProcedure(system) is not null ? $" The project's {system} has that name, but SQL Server runs its own procedure of that name instead, if it has one." : "";
            return new UnknowableCall(UnknowableCallKind.SystemProcedure, system, $"{text} runs {system}, which may be a system procedure, whose results are SQL Server's own.{also}", at);
        }

        if (name.ServerIdentifier is not null || name.DatabaseIdentifier is not null)
            return new UnknowableCall(UnknowableCallKind.MissingProcedure, null, $"{text} runs a procedure of another database, which is not in the project.", at);

        if (name.SchemaIdentifier is not null)
        {
            var objectName = SyntaxReader.Name(name);
            return Model.FindProcedure(objectName) is { } procedure
                ? new ProcedureCall(procedure, at)
                : new UnknowableCall(UnknowableCallKind.MissingProcedure, objectName, Missing(objectName, text), at);
        }

        // Observed: An_exec_or_dynamic_sql_in_a_procedure_follows_the_caller. The module's own schema is passed over.
        return SchemaNames.Resolve(Model.Procedures, own, SchemaSearch.Caller) switch
        {
            OneObject one => new ProcedureCall((Procedure)one.Named, at),
            SeveralObjects several => new UnknowableCall(UnknowableCallKind.AmbiguousProcedure, null,
                $"{text} names {own} without a schema, and {string.Join(" and ", several.Candidates.Select(c => c.Name))} have that name: which runs depends on the caller's default schema.", at),
            NoObject none => new UnknowableCall(UnknowableCallKind.MissingProcedure, none.InDbo, Missing(none.InDbo, text), at),
        };
    }

    private string Missing(ObjectName name, string text) => Model.FindReadable(name) is Synonym
        ? $"{text} runs the synonym {name}, which is not modelled yet."
        : $"{text} runs {name}, which is not a procedure of the project.{SchemaGaps.MayBeIn(Model)}";
}
