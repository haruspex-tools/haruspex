using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Syntax;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Semantics;

// Schema names written without a schema, and what an EXEC runs.
public sealed partial class Binder
{
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
        var candidates = ReadableNamed(name).Where(c => c.Kind != "function").Select(c => c.Name).Distinct().ToList();
        return candidates.Count > 0 ? candidates : [new ObjectName("dbo", name)];
    }

    /// <summary>
    /// Every object with this name, in any schema, that a FROM clause could read: tables, and the views,
    /// synonyms and functions. In a stable order.
    /// </summary>
    private List<(ObjectName Name, string Kind)> ReadableNamed(string name) =>
    [
        .. Model.Tables.Select(t => (t.Name, Kind: "table"))
            .Concat(Model.Readable.Select(o => (o.Name, Kind: KindName(o))))
            .Where(c => c.Name.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Name.ToString(), StringComparer.Ordinal)
            .ThenBy(c => c.Kind, StringComparer.Ordinal),
    ];

    /// <summary>What a view, synonym or function is, in a word for messages.</summary>
    private static string KindName(SchemaObject readable) => readable switch
    {
        View => "view",
        Synonym => "synonym",
        Function => "function",
        _ => throw new ArgumentException($"{readable.Name} is not an object a FROM clause reads besides a table.", nameof(readable)),
    };

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

        var candidates = Model.Procedures.Where(p => p.Name.Name.Equals(own, StringComparison.OrdinalIgnoreCase)).OrderBy(p => p.Name.ToString(), StringComparer.Ordinal).ToList();
        if (candidates.Count > 1)
        {
            return new UnknowableCall(UnknowableCallKind.AmbiguousProcedure, null,
                $"{text} names {own} without a schema, and {string.Join(" and ", candidates.Select(c => c.Name))} have that name: which runs depends on the caller's default schema.", at);
        }

        var dbo = SyntaxReader.Name(name);
        return candidates.Count == 1 ? new ProcedureCall(candidates[0], at) : new UnknowableCall(UnknowableCallKind.MissingProcedure, dbo, Missing(dbo, text), at);
    }

    private string Missing(ObjectName name, string text) => Model.FindReadable(name) is Synonym
        ? $"{text} runs the synonym {name}, which is not modelled yet."
        : $"{text} runs {name}, which is not a procedure of the project.";
}
