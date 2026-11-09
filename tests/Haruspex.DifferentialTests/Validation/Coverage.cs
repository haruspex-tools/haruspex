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
                Walk(document, results[i].Type, path, Need);
            }
        }

        missing.Sort(StringComparer.Ordinal);
        return missing;
    }

    private static void Walk(IrDocument document, IrType type, string path, Action<string, Seen> need)
    {
        switch (type)
        {
            case IrArray array:
                Walk(document, array.Items, path + "[]", need);
                break;
            case IrRef reference:
                foreach (var field in document.Types[reference.Name].Fields)
                {
                    if (field.Type is IrUnknownType)
                        continue;
                    var fieldPath = $"{path}.{field.Name}";
                    need(fieldPath, Seen.Value);
                    if (field.Nullable)
                        need(fieldPath, Seen.Null);
                    Walk(document, field.Type, fieldPath, need);
                }

                break;
        }
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
                WalkScalars(document, results[i].Type, $"results[{i}]", (path, scalar) =>
                {
                    foreach (var edge in Edges(scalar))
                    {
                        if (!saw.Contains((path, edge)))
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

    private static void WalkScalars(IrDocument document, IrType type, string path, Action<string, IrScalar> visit)
    {
        switch (type)
        {
            case IrArray array:
                WalkScalars(document, array.Items, path + "[]", visit);
                break;
            case IrRef reference:
                foreach (var field in document.Types[reference.Name].Fields)
                    WalkScalars(document, field.Type, $"{path}.{field.Name}", visit);
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
