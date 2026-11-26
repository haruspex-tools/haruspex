namespace Haruspex.Ir;

/// <summary>
/// Where a document first records a description (0.33): an object's, one of its parameters', or a field's. What
/// <see cref="IrDocument.Describes"/> asks, and where the reader says one is out of place in an older document.
/// </summary>
internal static class IrDocumentDescribed
{
    /// <summary>The path of the first description, objects before types; null when there is none.</summary>
    public static string? First(IrDocument document)
    {
        foreach (var (obj, i) in document.Objects.Select((o, i) => (o, i)))
        {
            if (obj.Description is not null)
                return $"$.objects[{i}].description";
            if (obj.Parameters.Select((p, j) => (p, j)).FirstOrDefault(p => p.p.Description is not null) is ({ }, var j))
                return $"$.objects[{i}].parameters[{j}].description";
        }

        foreach (var (name, type) in document.Types.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            if (type.Fields.Select((f, k) => (f, k)).FirstOrDefault(f => f.f.Description is not null) is ({ }, var k))
                return $"$.types{IrPath.Key(name)}.fields[{k}].description";
        }

        return null;
    }
}
