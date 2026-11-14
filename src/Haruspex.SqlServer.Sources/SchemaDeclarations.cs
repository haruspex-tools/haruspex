using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Sources;

/// <summary>
/// What a folder's scripts declare, as each <see cref="ScriptReader"/> reads it, before the passes that settle what is in
/// effect: every definition, every statement that changes one later, and every statement that may disable a constraint.
/// </summary>
/// <remarks>
/// It is the reading's one mutable state, written by the script readers in file order and read once, by
/// <see cref="ToModel"/>. A folder doesn't say the order its scripts run in, so nothing is applied while reading: a change is
/// recorded beside the definition it changes, and the passes decide what the model says of each.
/// </remarks>
internal sealed class SchemaDeclarations
{
    /// <summary>Problems found while reading: parse errors and objects defined twice.</summary>
    public List<Diagnostic> Diagnostics { get; } = [];

    /// <summary>The tables, by name, each as its <c>CREATE TABLE</c> declares it.</summary>
    public Dictionary<ObjectName, Table> Tables { get; } = [];

    /// <summary>The first statement that changes a table in a way the model does not apply, by table.</summary>
    public Dictionary<ObjectName, Redefinition> TableChanges { get; } = [];

    /// <summary>The alias and CLR types.</summary>
    public Dictionary<ObjectName, UserType> UserTypes { get; } = [];

    /// <summary>The procedures, each from its <c>CREATE</c> or <c>CREATE OR ALTER</c>.</summary>
    public Dictionary<ObjectName, Procedure> Procedures { get; } = [];

    /// <summary>The first <c>ALTER</c> or <c>DROP</c> of each procedure.</summary>
    public Dictionary<ObjectName, Change<Procedure>> ProcedureChanges { get; } = [];

    /// <summary>The functions, each from its <c>CREATE</c> or <c>CREATE OR ALTER</c>.</summary>
    public Dictionary<ObjectName, Function> Functions { get; } = [];

    /// <summary>The first <c>ALTER</c> or <c>DROP</c> of each function.</summary>
    public Dictionary<ObjectName, Change<Function>> FunctionChanges { get; } = [];

    /// <summary>Every definition of each view, in the order read.</summary>
    public Dictionary<ObjectName, List<ViewDefinition>> Views { get; } = [];

    /// <summary>The synonyms; of two of one name, the first read.</summary>
    public Dictionary<ObjectName, Synonym> Synonyms { get; } = [];

    /// <summary>Every definition of every DML trigger, in the order read.</summary>
    public List<Trigger> Triggers { get; } = [];

    /// <summary>The foreign keys with an action, in the order read.</summary>
    public List<CascadingKey> CascadingKeys { get; } = [];

    /// <summary>Every <c>ALTER TABLE … NOCHECK CONSTRAINT</c> and <c>… WITH CHECK CHECK CONSTRAINT</c>, wherever it is, in the order read.</summary>
    public List<Enforcement> Enforcement { get; } = [];

    /// <summary>Every <c>ALTER INDEX … DISABLE</c>, wherever it is.</summary>
    public List<IndexDisable> IndexDisables { get; } = [];

    /// <summary>Adds a definition, or reports a second one of the same name, keeping the first.</summary>
    public void Define<T>(Dictionary<ObjectName, T> into, ObjectName name, T definition, SourceLocation at)
    {
        ArgumentNullException.ThrowIfNull(into);
        if (!into.TryAdd(name, definition))
            Diagnostics.Add(new Diagnostic(SourceCodes.DefinedTwice, $"{name} is defined more than once.", at));
    }

    /// <summary>The model: each object's definition in effect, as the passes settle it, in the model's own order.</summary>
    /// <param name="settings">The database's settings, when the source records them (<see cref="DatabaseModel.Settings"/>).</param>
    /// <param name="unread">What the source refers to but couldn't read (<see cref="DatabaseModel.Unread"/>).</param>
    public DatabaseModel ToModel(DatabaseSettings? settings = null, IEnumerable<UnreadPart>? unread = null) => new(
        [.. Tables.Values.Select(t => ConstraintTrust.Trusted(t, Enforcement, IndexDisables))],
        UserTypes.Values,
        DefinitionsInEffect.Modules(Procedures, ProcedureChanges, (procedure, why) => procedure with { Redefinition = why }),
        DefinitionsInEffect.Modules(Functions, FunctionChanges, (function, why) => function with { Redefinition = why }),
        DefinitionsInEffect.Views(Views),
        Synonyms.Values,
        Triggers,
        CascadingKeys,
        TableChanges.Values)
    {
        Settings = settings,
        Unread = [.. unread ?? []],
    };
}
