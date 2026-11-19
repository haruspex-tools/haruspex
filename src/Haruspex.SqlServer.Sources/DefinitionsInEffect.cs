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
/// One script's <c>ALTER</c> of an object no other script names stands: it replaces whatever stood before it, and nothing the
/// project holds runs after it (<c>docs/decisions/0080-the-last-definition-in-a-script-stands.md</c>).
/// </remarks>
internal static class DefinitionsInEffect
{
    /// <summary>
    /// The procedures or functions: one a script defines and another changes keeps its definition for its parameters, with the
    /// first change recorded. One only altered, by one script, is its <c>ALTER</c>; by more, or also dropped, it is listed with
    /// its first <c>ALTER</c> when that is the first change, so that selecting it can report why its definition is not trusted.
    /// One only dropped is not listed.
    /// </summary>
    /// <param name="created">The definitions scripts make, one per object.</param>
    /// <param name="changes">Each script's change to each object.</param>
    /// <param name="changed">A definition with its change recorded.</param>
    public static IEnumerable<T> Modules<T>(Dictionary<ObjectName, T> created, Dictionary<ObjectName, List<Change<T>>> changes, Func<T, Redefinition, T> changed)
        where T : Module
    {
        foreach (var (name, definition) in created)
            yield return changes.TryGetValue(name, out var each) ? changed(definition, each[0].Why) : definition;
        foreach (var (name, each) in changes)
        {
            if (created.ContainsKey(name))
                continue;
            if (each is [{ Altered: { } only }])
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
}
