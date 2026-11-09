using Haruspex.Ir;

namespace Haruspex.DifferentialTests.Validation;

/// <summary>
/// Whether the seed data reached every case the IR allows. A field claimed non-null is only tested by data that
/// could expose it, so the runs must show each nullable field null at least once, each field with a value at
/// least once (so its type was checked), and each result that may be absent absent at least once.
/// </summary>
public static class Coverage
{
    /// <param name="seen">What the runs saw, by procedure id (<see cref="ValidationReport.Seen"/>, merged).</param>
    /// <returns>Each case not seen, as <c>procedure path: case</c>, sorted.</returns>
    public static IReadOnlyList<string> Missing(IrDocument document, IReadOnlyDictionary<string, IReadOnlySet<(string Path, Seen Seen)>> seen)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(seen);
        var missing = new List<string>();
        foreach (var procedure in document.Objects)
        {
            if (procedure.Results is not { } results)
                continue;

            var saw = seen.GetValueOrDefault(procedure.Id) ?? new HashSet<(string, Seen)>();
            void Need(string path, Seen what)
            {
                if (!saw.Contains((path, what)))
                    missing.Add($"{procedure.Id} {path}: {Describe(what)}");
            }

            for (var i = 0; i < results.Count; i++)
            {
                var path = $"results[{i}]";
                Need(path, Seen.Value);
                if (results[i].MayBeAbsent)
                    Need(path, Seen.Absent);
                Walk(document, results[i].Type, path, Need, []);
            }
        }

        missing.Sort(StringComparer.Ordinal);
        return missing;
    }

    /// <param name="walked">The recursive types already walked: each is walked once, under its name (<c>&lt;Node&gt;</c>), as the validator records it.</param>
    private static void Walk(IrDocument document, IrType type, string path, Action<string, Seen> need, HashSet<string> walked)
    {
        switch (type)
        {
            case IrArray array:
                // Items that may be null must be seen null and not; items never null need nothing more than FOR JSON's.
                if (array.ItemsNullable && array.Items is not IrUnknownType)
                {
                    need(path + "[]", Seen.Value);
                    need(path + "[]", Seen.Null);
                }

                Walk(document, array.Items, path + "[]", need, walked);
                break;
            case IrTuple tuple:
                for (var i = 0; i < tuple.Items.Count; i++)
                    Element(document, tuple.Items[i].Type, tuple.Items[i].Nullable, $"{path}[{i}]", need, walked);
                break;
            case IrRecord record:
                Element(document, record.Values, record.ValuesNullable, path + "{}", need, walked);
                break;
            case IrUnion union:
                foreach (var variant in union.Variants)
                    Walk(document, variant, path, need, walked);
                break;
            case IrJsonText { Content: { } content }:
                // What the text parses to is a claim like any other, checked at path<json>.
                Walk(document, content, path + "<json>", need, walked);
                break;
            case IrRef reference:
                var objectType = document.Types[reference.Name];
                if (objectType.Recursive)
                {
                    if (!walked.Add(reference.Name))
                        break;
                    path = $"<{reference.Name}>";
                }

                foreach (var field in objectType.Fields)
                {
                    if (field.Type is IrUnknownType)
                        continue;
                    var fieldPath = $"{path}.{field.Name}";
                    need(fieldPath, Seen.Value);
                    if (field.Nullable)
                        need(fieldPath, Seen.Null);
                    Walk(document, field.Type, fieldPath, need, walked);
                }

                break;
        }
    }

    private static void Element(IrDocument document, IrType type, bool nullable, string path, Action<string, Seen> need, HashSet<string> walked)
    {
        if (type is IrUnknownType)
            return;
        need(path, Seen.Value);
        if (nullable)
            need(path, Seen.Null);
        Walk(document, type, path, need, walked);
    }

    /// <summary>
    /// The edges of the IR's facets that no run reached (see <see cref="ValidationReport.Edges"/>). A bound, a
    /// length or a value set is only tested by data at its edge: a maximum of 255 is not shown to hold by values of
    /// 7. So each bound must be met (or, when exclusive, the nearest step inside it), each length filled, each
    /// minimum length met and each value of a set seen. Under an exact set the bounds are not required, since the
    /// set already rules most of the range out (a smallint limited to 1, 2 and 4 never reaches -32768).
    /// </summary>
    /// <returns>Each edge not reached, as <c>procedure path: edge</c>, sorted.</returns>
    public static IReadOnlyList<string> MissingEdges(IrDocument document, IReadOnlyDictionary<string, IReadOnlySet<(string Path, string Edge)>> reached)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(reached);
        var missing = new List<string>();
        foreach (var procedure in document.Objects)
        {
            if (procedure.Results is not { } results)
                continue;

            var saw = reached.GetValueOrDefault(procedure.Id) ?? new HashSet<(string, string)>();
            for (var i = 0; i < results.Count; i++)
            {
                WalkScalars(document, results[i].Type, $"results[{i}]", [], (path, scalar) =>
                {
                    foreach (var edge in Edges(scalar))
                    {
                        if (!saw.Contains((path, edge)))
                            missing.Add($"{procedure.Id} {path}: {edge}");
                    }
                }, (path, array) =>
                {
                    // An array's length is an edge like a string's: json_array's bounds are only tested by data at them.
                    foreach (var edge in new[] { array.MinItems is { } min ? $"min items {min}" : null, array.MaxItems is { } max ? $"max items {max}" : null })
                    {
                        if (edge is not null && !saw.Contains((path, edge)))
                            missing.Add($"{procedure.Id} {path}: {edge}");
                    }
                });
            }
        }

        missing.Sort(StringComparer.Ordinal);
        return missing;
    }

    private static IEnumerable<string> Edges(IrScalar scalar)
    {
        if (scalar.Facets is not { } facets)
            yield break;
        var exactSet = facets.Values is { Match: IrValueMatch.Exact };
        var stepKnown = facets.Digits is not null || scalar.Format is IrFormats.Uint8 or IrFormats.Int16 or IrFormats.Int32 or IrFormats.Int64;
        if (!exactSet && facets.Min is { } min && (min.Inclusive || stepKnown))
            yield return "min";
        if (!exactSet && facets.Max is { } max && (max.Inclusive || stepKnown))
            yield return "max";
        if (facets.Length is { } length)
            yield return $"length {length.Value}";
        if (facets.MinLength is { } minLength)
            yield return $"min length {minLength.Value}";
        foreach (var item in facets.Values?.Items ?? [])
            yield return "value " + item.Text;
    }

    private static void WalkScalars(IrDocument document, IrType type, string path, HashSet<string> walked, Action<string, IrScalar> visit, Action<string, IrArray> arrays)
    {
        switch (type)
        {
            case IrArray array:
                arrays(path, array);
                WalkScalars(document, array.Items, path + "[]", walked, visit, arrays);
                break;
            case IrTuple tuple:
                for (var i = 0; i < tuple.Items.Count; i++)
                    WalkScalars(document, tuple.Items[i].Type, $"{path}[{i}]", walked, visit, arrays);
                break;
            case IrRecord record:
                WalkScalars(document, record.Values, path + "{}", walked, visit, arrays);
                break;
            case IrUnion union:
                foreach (var variant in union.Variants)
                    WalkScalars(document, variant, path, walked, visit, arrays);
                break;
            case IrJsonText { Content: { } content }:
                WalkScalars(document, content, path + "<json>", walked, visit, arrays);
                break;
            case IrRef reference:
                var objectType = document.Types[reference.Name];
                if (objectType.Recursive)
                {
                    if (!walked.Add(reference.Name))
                        break;
                    path = $"<{reference.Name}>";
                }

                foreach (var field in objectType.Fields)
                    WalkScalars(document, field.Type, $"{path}.{field.Name}", walked, visit, arrays);
                break;
            case IrScalar scalar:
                visit(path, scalar);
                break;
        }
    }

    private static string Describe(Seen seen) => seen switch
    {
        Seen.Null => "null",
        Seen.Value => "a value",
        _ => "absent",
    };
}
