namespace Haruspex.Ir;

/// <summary>
/// Where a document first says what makes a reason so (0.29): a <see cref="IrNullReason.Detail"/>. What
/// <see cref="IrDocument.Explains"/> asks, and where the reader says a detail is out of place in an older document.
/// </summary>
internal static class IrDocumentExplained
{
    /// <summary>The path of the first reason with a detail, in the order of the file; null when there is none.</summary>
    public static string? First(IrDocument document)
    {
        foreach (var (obj, i) in document.Objects.Select((o, i) => (o, i)))
        {
            foreach (var (result, k) in (obj.Results ?? []).Select((r, k) => (r, k)))
            {
                if (In(result.AbsentBecause, $"$.objects[{i}].results[{k}].absentBecause") is { } absent)
                    return absent;
            }
        }

        foreach (var (name, type) in document.Types.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            foreach (var (field, i) in type.Fields.Select((f, i) => (f, i)))
            {
                if (In(field.NullableBecause, $"$.types{IrPath.Key(name)}.fields[{i}].nullableBecause") is { } path)
                    return path;
            }
        }

        foreach (var (path, node) in IrDocumentScalars.Nodes(document))
        {
            var found = node switch
            {
                IrArray array => In(array.ItemsNullableBecause ?? [], path + ".itemsNullableBecause"),
                IrRecord record => In(record.ValuesNullableBecause, path + ".valuesNullableBecause"),
                IrTuple tuple => tuple.Items.Select((e, j) => In(e.NullableBecause, $"{path}.items[{j}].nullableBecause")).FirstOrDefault(p => p is not null),
                _ => null,
            };
            if (found is not null)
                return found;
        }

        return null;

        static string? In(IEnumerable<IrNullReason> reasons, string path)
        {
            var i = 0;
            foreach (var reason in reasons)
            {
                if (reason.Detail is not null)
                    return $"{path}[{i}].detail";
                i++;
            }

            return null;
        }
    }
}
