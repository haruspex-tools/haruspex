namespace Haruspex.Ir;

/// <summary>
/// Why a field is what it is, in sentences for the reader of generated code: each emitter writes the same lines in its own
/// place (a Zod or TypeScript comment, a JSON Schema <c>description</c>), so that every emitter says the same of the same
/// field. Each line is a sentence; the last names the <c>haruspex explain</c> command that says more, with where each rule
/// applied, which a comment leaves out so that it changes only when the reason does
/// (<c>docs/decisions/0079-a-surprise-is-explained-where-it-is-written.md</c>).
/// </summary>
public static class IrFieldText
{
    /// <summary>
    /// Why a key may be left out when its value is null, in the words every emitter and <c>haruspex explain</c> use. It names both
    /// constructs that do it, since the IR doesn't record which of them built an object: <c>FOR JSON</c> (without
    /// <c>INCLUDE_NULL_VALUES</c>) and <c>JSON_OBJECT</c> with <c>ABSENT ON NULL</c>.
    /// </summary>
    public const string KeyLeftOutWhenNull = "FOR JSON, and JSON_OBJECT with ABSENT ON NULL, leave out a key whose value is null";

    /// <summary>
    /// Why a key may be left out whatever its value (<see cref="IrPresence.Optional"/>), in <paramref name="document"/>: one of the
    /// two databases a combined document holds against doesn't return it, or leaves it out when null where the other sends null;
    /// or (0.28) the schema a column is declared with doesn't require it; or (0.36) a REST endpoint's reply had no body, or the schema its
    /// response is declared with doesn't require it. In the words every emitter and <c>haruspex explain</c> use.
    /// </summary>
    public static string LeftOutWhateverItsValue(IrDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        const string Combined = "one of the two databases doesn't return the key, or leaves it out when null where the other sends null";
        const string Declared = "the schema a column is declared to hold doesn't require the key";

        // A REST endpoint's envelope leaves result out when the reply has no body, declared or not (0.36), so a document of one
        // database that declares no column's JSON may still hold an optional key.
        const string Rest = "the reply of a REST endpoint had no body, or the schema its response is declared with doesn't require the key";
        var combined = document.Source.Baseline is not null;
        var columns = document.Columns.Count > 0;
        string[] why = [.. combined ? [Combined] : (string[])[], .. columns ? [Declared] : (string[])[], .. document.Endpoints.Count > 0 || (!combined && !columns) ? [Rest] : (string[])[]];
        return string.Join("; or ", why);
    }

    /// <summary>
    /// The lines that explain a field of <paramref name="typeName"/> at the level <paramref name="comments"/> asks for: none for a
    /// field that is no surprise unless every field is explained, and none at all under <see cref="IrComments.None"/>.
    /// </summary>
    public static IReadOnlyList<string> Of(IrDocument document, string typeName, IrField field, IrComments comments)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(field);
        var surprise = IrSurprises.Of(field);
        if (comments == IrComments.None || (comments == IrComments.Surprises && !surprise))
            return [];

        var lines = new List<string>();
        var ways = new List<string>();
        // An unknown's code is already in the note every emitter writes on it; its way out is this comment's to give.
        if (field.Type is IrUnknownType unknown)
            ways.Add(IrReasons.WayOut(unknown.Reason));

        if (IrSurprises.TypeDiffers(field) && field.Type is IrScalar { Sql: { } sql })
        {
            var columns = List([.. field.From.Where(c => c.Sql is not null).Select(c => $"{c.Column} is {c.Sql}")]);
            lines.Add($"Its type is {sql}, where {columns}: what it is computed with makes it another type.");
        }
        else if (comments == IrComments.All && field.From.Count > 0)
        {
            lines.Add($"Read from {List([.. field.From.Select(c => c.Column)])}.");
        }

        var reasons = comments == IrComments.All ? [.. field.NullableBecause] : IrSurprises.Reasons(field);
        // An unknown's own null reason says what its line above already has.
        foreach (var rule in reasons.Select(r => r.Rule).Where(r => r != IrNullRule.Unknown || field.Type is not IrUnknownType).Distinct())
        {
            // What made it so, where a reason says (a JSON path's steps, 0.29), once each.
            var details = reasons.Where(r => r.Rule == rule && r.Detail is not null).Select(r => r.Detail).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
            lines.Add($"May be null: {IrReasons.Text(rule)} ({IrWireNames.Of(rule)}){(details.Count > 0 ? ": " + string.Join("; ", details) : "")}.");
            if (IrReasons.WayOut(rule) is { } way)
                ways.Add(way);
        }

        if (field.Presence == IrPresence.Optional)
            lines.Add($"May be left out: {LeftOutWhateverItsValue(document)}.");
        else if (IrSurprises.AbsentWhenNull(field))
            lines.Add($"Left out when null, as {KeyLeftOutWhenNull}.");

        lines.AddRange(ways.Distinct(StringComparer.Ordinal).Select(w => "To change it: " + w + "."));
        lines.Add("Why: " + IrFieldPlaces.Command(document, typeName, field));
        return lines;
    }

    /// <summary>
    /// The lines that explain an object's results, for the comment on its function: why a result set may be missing, or why they
    /// can't be listed, and under <see cref="IrComments.All"/> how many rows each returns. None when there is nothing to say.
    /// </summary>
    public static IReadOnlyList<string> Of(IrDocument document, IrObject obj, IrComments comments)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(obj);
        if (comments == IrComments.None)
            return [];

        var lines = new List<string>();
        if (obj.Results is not { } results)
        {
            foreach (var unknown in document.Unknowns.Where(u => u.Object == obj.Id && u.Path == "results").Select(u => u.Reason).Distinct())
                lines.Add($"Its result sets can't be listed: {IrReasons.Text(unknown)} ({IrWireNames.Of(unknown)}). To change it: {IrReasons.WayOut(unknown)}.");
        }
        else
        {
            foreach (var (result, i) in results.Select((r, i) => (r, i)))
            {
                var set = results.Count == 1 ? "Its result" : $"The result set {result.Name ?? ("#" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture))}";
                foreach (var rule in result.AbsentBecause.Select(r => r.Rule).Distinct())
                    lines.Add($"{set} may be missing: {IrReasons.Text(rule)} ({IrWireNames.Of(rule)}).");
                if (comments == IrComments.All)
                    lines.Add($"{set} has {IrReasons.Text(result.Cardinality)} ({IrWireNames.Of(result.Cardinality)}).");
            }
        }

        if (lines.Count > 0)
            lines.Add("Why: " + IrFieldPlaces.Command(obj));
        return lines;
    }

    /// <summary>Items as English lists them: <c>a</c>, <c>a and b</c>, <c>a, b and c</c>.</summary>
    private static string List(IReadOnlyList<string> items) =>
        items.Count <= 1 ? string.Concat(items) : string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1];
}
