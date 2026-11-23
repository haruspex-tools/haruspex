using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Sources;

/// <summary>
/// A pass over what the scripts declare, once each script has been played in its own order (<see cref="ScriptDefinitions{T}"/>):
/// which definition of each procedure, function and view the model keeps, and the doubt it records when more than one script
/// defines or changes it.
/// </summary>
/// <remarks>
/// A folder doesn't say the order its scripts run in, so of a definition in one script and an <c>ALTER</c> or <c>DROP</c> in
/// another, which is in effect is unknown. The model records the doubt (<see cref="Redefinition"/>) rather than guess an order.
/// One script's <c>ALTER</c> of a procedure or view no other script names stands: it replaces whatever stood before it, and
/// nothing the project holds runs after it. A function's doesn't: what stood may be of another kind, which the server refuses to
/// change (2010), and an unseen stub's kind is unknown (<c>docs/decisions/0080-the-last-definition-in-a-script-stands.md</c>).
/// </remarks>
internal static class DefinitionsInEffect
{
    /// <summary>
    /// The procedures or functions: one a script defines and another changes keeps its definition for its parameters, with the
    /// first change recorded. One only altered, by one script, is its <c>ALTER</c> when <paramref name="alterAloneStands"/>; by
    /// more, or also dropped, or a function, it is listed with
    /// its first <c>ALTER</c> when that is the first change, so that selecting it can report why its definition is not trusted.
    /// One only dropped is not listed.
    /// </summary>
    /// <param name="created">The definitions scripts make, one per object.</param>
    /// <param name="changes">Each script's change to each object.</param>
    /// <param name="changed">A definition with its change recorded.</param>
    /// <param name="alterAloneStands">Whether one script's <c>ALTER</c> alone stands: for a procedure, not a function.</param>
    public static IEnumerable<T> Modules<T>(Dictionary<ObjectName, T> created, Dictionary<ObjectName, List<Change<T>>> changes, Func<T, Redefinition, T> changed, bool alterAloneStands)
        where T : Module
    {
        foreach (var (name, definition) in created)
            yield return changes.TryGetValue(name, out var each) ? changed(definition, each[0].Why) : definition;
        foreach (var (name, each) in changes)
        {
            if (created.ContainsKey(name))
                continue;
            if (alterAloneStands && each is [{ Altered: { } only }])
                yield return only;
            else if (each[0].Altered is { } altered)
                yield return changed(altered, each[0].Why);
        }
    }

    /// <summary>
    /// The views: one defined by one script keeps the definition that script leaves; one defined by two keeps its first and
    /// records its second, since which applies depends on the order the scripts run.
    /// </summary>
    public static IEnumerable<View> Views(Dictionary<ObjectName, List<ViewDefinition>> definitions) =>
        definitions.Values.Select(each => each is [var only]
            ? only.View
            : each[0].View with { Redefinition = new Redefinition(each[0].View.Name, each[1].Statement, each[1].View.Span.Start) });

    /// <summary>
    /// What a deployment leaves of each procedure, function or view, by name: the first statement of a deployment script that
    /// changes it, or, when none does, the first deployment script that names it and can't be read; null when neither.
    /// </summary>
    /// <param name="deployed">Each module's first change in a deployment script.</param>
    /// <param name="unread">The deployment scripts not read in full.</param>
    public static Func<ObjectName, Redefinition?> Deployed(Dictionary<ObjectName, Redefinition> deployed, List<UnreadDeployment> unread) =>
        name => deployed.TryGetValue(name, out var why) ? why
            : unread.FirstOrDefault(u => u.Names(name)) is { } script ? new Redefinition(name, RedefiningStatement.UnreadScript, script.At)
            : null;

    /// <summary>
    /// The changes to tables that only the whole model settles, for each table no statement changed outright: a dropped period,
    /// when the table has a hidden column the drop makes visible; otherwise a deployment script not read in full that names it.
    /// </summary>
    public static IEnumerable<Redefinition> TablesChangedAfter(
        Dictionary<ObjectName, Table> tables, Dictionary<ObjectName, Redefinition> changed, List<Redefinition> periodDrops, List<UnreadDeployment> unread)
    {
        foreach (var (name, table) in tables)
        {
            if (changed.ContainsKey(name))
                continue;
            if (table.Columns.Any(c => c.IsHidden) && periodDrops.FirstOrDefault(d => d.Name == name) is { } drop)
                yield return drop;
            else if (unread.FirstOrDefault(u => u.Names(name)) is { } script)
                yield return new Redefinition(name, RedefiningStatement.UnreadScript, script.At);
        }
    }
}
