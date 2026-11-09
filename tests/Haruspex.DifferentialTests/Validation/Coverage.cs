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

    private static string Describe(Seen seen) => seen switch
    {
        Seen.Null => "null",
        Seen.Value => "a value",
        _ => "absent",
    };
}
