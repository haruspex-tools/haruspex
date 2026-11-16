using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Syntax;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Semantics;

// Schema names written without a schema, and what an EXEC runs.
public sealed partial class Binder
{
    /// <summary>
    /// The local temporary table a name written as a table means: its name, when it is of one part and starts with one
    /// <c>#</c>; null for any other. A global one (<c>##t</c>) is shared with every session, which may change it at any time,
    /// so it is read as a permanent table is. The walk keeps what each holds by this name, and a body can create a given
    /// <c>#t</c> once (observed: A_procedure_cannot_create_a_temporary_table_twice_even_after_dropping_it).
    /// </summary>
    /// <param name="name">The name as a statement writes it: <c>CREATE TABLE</c>, <c>DROP TABLE</c>, <c>SELECT … INTO</c>, a FROM.</param>
    public static LocalName? Temporary(SchemaObjectName name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name is { ServerIdentifier: null, DatabaseIdentifier: null, SchemaIdentifier: null, BaseIdentifier.Value: var own }
            && own.StartsWith('#') && !own.StartsWith("##", StringComparison.Ordinal)
            ? new LocalName(own)
            : null;
    }

    /// <summary>
    /// The objects a name written as the target of a write may mean, by the rule <see cref="Bind"/> resolves table names
    /// with: a schema-qualified name means itself; an unqualified one, the one table, view or synonym of that name if exactly
    /// one schema has it, every one of them if more do (which is written depends on the caller's default schema), and
    /// <c>dbo</c>'s when none does. Empty for a name in another database or on a linked server, which the project cannot show.
    /// </summary>
    /// <param name="name">The name as written; a temporary table's or a body's own relation's is not asked about.</param>
    public IReadOnlyList<ObjectName> ResolveWritten(SchemaObjectName name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return ResolveWritten(name.ServerIdentifier is not null || name.DatabaseIdentifier is not null, name.SchemaIdentifier?.Value, name.BaseIdentifier.Value);
    }

    /// <summary>The objects a synonym's name may mean, by the rule <see cref="ResolveWritten(SchemaObjectName)"/> gives.</summary>
    /// <param name="target">The name the synonym stands for.</param>
    public IReadOnlyList<ObjectName> ResolveWritten(SynonymTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return ResolveWritten(target.Server is not null || target.Database is not null, target.Schema, target.Name);
    }

    private List<ObjectName> ResolveWritten(bool elsewhere, string? schema, string name)
    {
        if (elsewhere)
            return [];
        if (schema is not null)
            return [new ObjectName(schema, name)];
        // A function is read, never written: every table, view or synonym of the name may be what is written.
        return SchemaNames.Resolve(Readable().Where(o => o is not Function), name) switch
        {
            OneObject one => [one.Named.Name],
            SeveralObjects several => [.. several.Candidates.Select(c => c.Name)],
            NoObject none => [none.InDbo],
        };
    }

    /// <summary>Every object a FROM clause could read: the tables, and the views, synonyms and functions.</summary>
    private IEnumerable<SchemaObject> Readable() => Model.Tables.Cast<SchemaObject>().Concat(Model.Readable);

    // ---- EXEC ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Binds what an <c>EXEC</c> runs: a procedure of the project, or why its results cannot be known from the project. An
    /// unqualified name resolves like a table's (see <see cref="Bind"/>): to the one procedure of that name, if exactly
    /// one schema has it, since SQL Server looks in the caller's default schema and then in <c>dbo</c>.
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

        return SchemaNames.Resolve(Model.Procedures, own) switch
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
