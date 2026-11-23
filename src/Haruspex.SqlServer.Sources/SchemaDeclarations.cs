using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Sources;

/// <summary>
/// What a folder's scripts declare, as each <see cref="ScriptReader"/> reads it, before the passes that settle what is in
/// effect: every definition, every statement that changes one later, and every statement that may disable a constraint.
/// </summary>
/// <remarks>
/// It is the reading's one mutable state, written by the script readers in file order and read once, by
/// <see cref="ToModel"/>. A folder doesn't say the order its scripts run in, so nothing is applied across scripts while
/// reading: each script adds what its own order leaves (<see cref="Settle{T}"/>), a change is recorded beside the definition it
/// changes, and the passes decide what the model says of each.
/// </remarks>
internal sealed class SchemaDeclarations
{
    /// <summary>Problems found while reading: parse errors and objects defined twice.</summary>
    public List<Diagnostic> Diagnostics { get; } = [];

    /// <summary>The tables, by name, each as its <c>CREATE TABLE</c> declares it.</summary>
    public Dictionary<ObjectName, Table> Tables { get; } = [];

    /// <summary>The first statement that changes a table in a way the model does not apply, by table.</summary>
    public Dictionary<ObjectName, Redefinition> TableChanges { get; } = [];

    /// <summary>
    /// Every <c>ALTER TABLE … DROP PERIOD FOR SYSTEM_TIME</c>, in the order read: a change only to a table with a hidden column,
    /// which it makes visible (<c>WeakeningStatements</c>).
    /// </summary>
    public List<Redefinition> PeriodDrops { get; } = [];

    /// <summary>The primary keys and unique constraints <c>ALTER TABLE … ADD CONSTRAINT</c> adds, in the order read.</summary>
    public List<AddedKey> AddedKeys { get; } = [];

    /// <summary>The alias and CLR types.</summary>
    public Dictionary<ObjectName, UserType> UserTypes { get; } = [];

    /// <summary>The procedures, each from its <c>CREATE</c> or <c>CREATE OR ALTER</c>.</summary>
    public Dictionary<ObjectName, Procedure> Procedures { get; } = [];

    /// <summary>Each script's change to each procedure, in the order read (<see cref="Change{T}"/>).</summary>
    public Dictionary<ObjectName, List<Change<Procedure>>> ProcedureChanges { get; } = [];

    /// <summary>The functions, each from its <c>CREATE</c> or <c>CREATE OR ALTER</c>.</summary>
    public Dictionary<ObjectName, Function> Functions { get; } = [];

    /// <summary>Each script's change to each function, in the order read (<see cref="Change{T}"/>).</summary>
    public Dictionary<ObjectName, List<Change<Function>>> FunctionChanges { get; } = [];

    /// <summary>Each script's definition of each view, in the order read.</summary>
    public Dictionary<ObjectName, List<ViewDefinition>> Views { get; } = [];

    /// <summary>
    /// The synonyms, each as its script leaves it (a synonym dropped and created again is its last <c>CREATE</c>); a second
    /// definition of one name is reported (<see cref="Define{T}"/>).
    /// </summary>
    public Dictionary<ObjectName, Synonym> Synonyms { get; } = [];

    /// <summary>
    /// Every <c>CREATE SYNONYM</c> that isn't a batch's own statement, read wherever it is: under <c>IF</c>, in a block or in a body,
    /// or in a deployment script. Whether it runs isn't knowable, so it may (<see cref="SynonymsInEffect"/>).
    /// </summary>
    public List<Synonym> ConditionalSynonyms { get; } = [];

    /// <summary>Every definition of every DML trigger, in the order read.</summary>
    public List<Trigger> Triggers { get; } = [];

    /// <summary>The foreign keys with an action, in the order read.</summary>
    public List<CascadingKey> CascadingKeys { get; } = [];

    /// <summary>Every <c>ALTER TABLE … NOCHECK CONSTRAINT</c> and <c>… WITH CHECK CHECK CONSTRAINT</c>, wherever it is, in the order read.</summary>
    public List<Enforcement> Enforcement { get; } = [];

    /// <summary>Every <c>ALTER INDEX … DISABLE</c>, <c>ALTER TABLE … DROP CONSTRAINT</c> and <c>DROP INDEX</c>, wherever it is.</summary>
    public List<KeyDisable> KeyDisables { get; } = [];

    /// <summary>
    /// The first statement of a pre- or post-deployment script that creates, alters or drops each procedure, function or view: a
    /// deployment runs it after the model is deployed, so the definition the model holds is not the one the database is left with.
    /// </summary>
    public Dictionary<ObjectName, Redefinition> Deployed { get; } = [];

    /// <summary>The deployment scripts that couldn't be read in full, each with the names in it that are doubted.</summary>
    public List<UnreadDeployment> UnreadDeployments { get; } = [];

    /// <summary>Adds a definition, or reports a second one of the same name, keeping the first.</summary>
    public void Define<T>(Dictionary<ObjectName, T> into, ObjectName name, T definition, SourceLocation at)
    {
        ArgumentNullException.ThrowIfNull(into);
        if (!into.TryAdd(name, definition))
            Diagnostics.Add(new Diagnostic(SourceCodes.DefinedTwice, $"{name} is defined more than once.", at));
    }

    /// <summary>
    /// What one script leaves of each procedure or function it names: a definition it makes is defined (a second, from another
    /// script, is reported), with the doubt it leaves as a change; a definition it only alters, or none, is a change.
    /// </summary>
    public void Settle<T>(IEnumerable<ScriptOutcome<T>> outcomes, Dictionary<ObjectName, T> created, Dictionary<ObjectName, List<Change<T>>> changes)
        where T : Module
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        ArgumentNullException.ThrowIfNull(created);
        ArgumentNullException.ThrowIfNull(changes);
        foreach (var outcome in outcomes)
        {
            if (outcome is { Standing: { } made, Established: true, At: { } at })
            {
                Define(created, outcome.Name, made, at);
                if (outcome.Why is { } doubt)
                    Changes(changes, outcome.Name).Add(new Change<T>(doubt, null));
            }
            else if (outcome.Why is { } why)
            {
                Changes(changes, outcome.Name).Add(new Change<T>(why, outcome.Standing));
            }
        }
    }

    /// <summary>
    /// What one script leaves of each view it names: the definition that stands, with the doubt it leaves. A view the script
    /// drops last is not followed, as no drop of a view is elsewhere.
    /// </summary>
    public void Settle(IEnumerable<ScriptOutcome<ViewDefinition>> outcomes)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        foreach (var outcome in outcomes)
        {
            if (outcome.Standing is not { } standing)
                continue;
            var definition = outcome.Established && outcome.Why is { } doubt ? standing with { View = standing.View with { Redefinition = doubt } } : standing;
            if (!Views.TryGetValue(outcome.Name, out var definitions))
                Views[outcome.Name] = definitions = [];
            definitions.Add(definition);
        }
    }

    /// <summary>
    /// The synonyms the model holds. A synonym is the definition its batches leave, unless a <c>CREATE SYNONYM</c> that may run
    /// (<see cref="ConditionalSynonyms"/>) names another target: then which target stands isn't known, and the synonym is left
    /// out, so a name read through it is unresolved rather than typed from one of them. One that only such statements create, all
    /// of them with one target, is that target: if none runs there is no synonym, and a read through it fails rather than reads
    /// another object. A synonym has no record of a doubt (<c>docs/decisions/0112-a-script-is-parsed-as-the-server-parses-each-batch.md</c>).
    /// </summary>
    private IEnumerable<Synonym> SynonymsInEffect()
    {
        var conditional = ConditionalSynonyms.GroupBy(s => s.Name).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var synonym in Synonyms.Values)
        {
            if (!conditional.Remove(synonym.Name, out var others) || others.All(o => SameTarget(o.For, synonym.For)))
                yield return synonym;
        }

        foreach (var created in conditional.Values)
        {
            if (created.All(o => SameTarget(o.For, created[0].For)))
                yield return created[0];
        }
    }

    /// <summary>Whether two synonyms stand for the same name, compared part by part as SQL Server compares names, ignoring case.</summary>
    private static bool SameTarget(SynonymTarget one, SynonymTarget other) =>
        string.Equals(one.Server, other.Server, StringComparison.OrdinalIgnoreCase)
        && string.Equals(one.Database, other.Database, StringComparison.OrdinalIgnoreCase)
        && string.Equals(one.Schema, other.Schema, StringComparison.OrdinalIgnoreCase)
        && string.Equals(one.Name, other.Name, StringComparison.OrdinalIgnoreCase);

    private static List<Change<T>> Changes<T>(Dictionary<ObjectName, List<Change<T>>> changes, ObjectName name)
        where T : Module
    {
        if (!changes.TryGetValue(name, out var list))
            changes[name] = list = [];
        return list;
    }

    /// <summary>The model: each object's definition in effect, as the passes settle it, in the model's own order.</summary>
    /// <param name="settings">The database's settings, when the source records them (<see cref="DatabaseModel.Settings"/>).</param>
    /// <param name="unread">What the source refers to but couldn't read (<see cref="DatabaseModel.Unread"/>).</param>
    /// <param name="databases">The other databases the source holds the schema of (<see cref="DatabaseModel.Databases"/>).</param>
    public DatabaseModel ToModel(DatabaseSettings? settings = null, IEnumerable<UnreadPart>? unread = null, IReadOnlyDictionary<string, DatabaseModel>? databases = null)
    {
        var deployed = DefinitionsInEffect.Deployed(Deployed, UnreadDeployments);
        return new(
            [.. Tables.Values.Select(t => ConstraintTrust.Trusted(AddedKey.Into(t, AddedKeys), Enforcement, KeyDisables))],
            UserTypes.Values,
            DefinitionsInEffect.Modules(Procedures, ProcedureChanges, (procedure, why) => procedure with { Redefinition = why }, alterAloneStands: true)
                .Select(p => p.Redefinition is null && deployed(p.Name) is { } why ? p with { Redefinition = why } : p),
            DefinitionsInEffect.Modules(Functions, FunctionChanges, (function, why) => function with { Redefinition = why }, alterAloneStands: false)
                .Select(f => f.Redefinition is null && deployed(f.Name) is { } why ? f with { Redefinition = why } : f),
            DefinitionsInEffect.Views(Views).Select(v => v.Redefinition is null && deployed(v.Name) is { } why ? v with { Redefinition = why } : v),
            SynonymsInEffect(),
            Triggers,
            CascadingKeys,
            [.. TableChanges.Values, .. DefinitionsInEffect.TablesChangedAfter(Tables, TableChanges, PeriodDrops, UnreadDeployments)])
        {
            Settings = settings,
            DefaultCollation = settings?.Collation,
            Unread = [.. unread ?? []],
            Databases = databases ?? new Dictionary<string, DatabaseModel>(),
        };
    }
}
