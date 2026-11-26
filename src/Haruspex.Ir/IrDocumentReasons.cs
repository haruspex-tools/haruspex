namespace Haruspex.Ir;

/// <summary>
/// Every reason a document gives why something may be null or absent, with its JSON path in the document's file, in the order of
/// the file: results' reasons for being absent, named types' fields', then those nested in arrays, records and tuples. Where a
/// version labelled by a reason's content (<see cref="IrDocument.Explains"/>, <see cref="IrDocument.HoldsXml"/>) finds it.
/// </summary>
internal static class IrDocumentReasons
{
    /// <summary>Each reason of <paramref name="document"/> and its path.</summary>
    public static IEnumerable<(string Path, IrNullReason Reason)> Of(IrDocument document)
    {
        foreach (var (obj, i) in document.Objects.Select((o, i) => (o, i)))
        {
            foreach (var (result, k) in (obj.Results ?? []).Select((r, k) => (r, k)))
            {
                foreach (var found in In(result.AbsentBecause, $"$.objects[{i}].results[{k}].absentBecause"))
                    yield return found;
            }
        }

        foreach (var (name, type) in document.Types.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            foreach (var (field, i) in type.Fields.Select((f, i) => (f, i)))
            {
                foreach (var found in In(field.NullableBecause, $"$.types{IrPath.Key(name)}.fields[{i}].nullableBecause"))
                    yield return found;
            }
        }

        foreach (var (path, node) in IrDocumentScalars.Nodes(document))
        {
            var nested = node switch
            {
                IrArray array => In(array.ItemsNullableBecause ?? [], path + ".itemsNullableBecause"),
                IrRecord record => In(record.ValuesNullableBecause, path + ".valuesNullableBecause"),
                IrTuple tuple => tuple.Items.SelectMany((e, j) => In(e.NullableBecause, $"{path}.items[{j}].nullableBecause")),
                _ => [],
            };
            foreach (var found in nested)
                yield return found;
        }
    }

    private static IEnumerable<(string Path, IrNullReason Reason)> In(IEnumerable<IrNullReason> reasons, string path) =>
        reasons.Select((reason, i) => ($"{path}[{i}]", reason));
}
