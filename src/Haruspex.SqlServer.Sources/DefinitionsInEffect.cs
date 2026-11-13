using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Sources;

/// <summary>
/// A pass over what the scripts declare: which definition of each procedure, function and view the model keeps, and the
/// doubt it records when the source also changes one.
/// </summary>
/// <remarks>
/// A folder doesn't say the order its scripts run in, so of a definition and a later <c>ALTER</c> or <c>DROP</c>, which is in
/// effect is unknown. The model records the doubt (<see cref="Redefinition"/>) rather than guess an order.
/// </remarks>
internal static class DefinitionsInEffect
{
    /// <summary>
    /// The procedures or functions: one also altered or dropped keeps its <c>CREATE</c> for its parameters, with the first
    /// change recorded. One only altered is listed with its <c>ALTER</c>, so that selecting it can report why its definition is
    /// not trusted. One only dropped is not listed.
    /// </summary>
    /// <param name="created">The definitions read from <c>CREATE</c> and <c>CREATE OR ALTER</c>.</param>
    /// <param name="changes">The first change of each.</param>
    /// <param name="changed">A definition with its change recorded.</param>
    public static IEnumerable<T> Modules<T>(Dictionary<ObjectName, T> created, Dictionary<ObjectName, Change<T>> changes, Func<T, Redefinition, T> changed)
        where T : Module
    {
        foreach (var (name, definition) in created)
            yield return changes.TryGetValue(name, out var change) ? changed(definition, change.Why) : definition;
        foreach (var (name, change) in changes)
        {
            if (!created.ContainsKey(name) && change.Altered is { } altered)
                yield return changed(altered, change.Why);
        }
    }

    /// <summary>
    /// The views: one defined once keeps its definition; one created and altered, or created twice, keeps its first and
    /// records its second, since which applies depends on the order the scripts run.
    /// </summary>
    public static IEnumerable<View> Views(Dictionary<ObjectName, List<ViewDefinition>> definitions) =>
        definitions.Values.Select(each => each is [var only]
            ? only.View
            : each[0].View with { Redefinition = new Redefinition(each[0].View.Name, each[1].Statement, each[1].View.Span.Start) });
}
