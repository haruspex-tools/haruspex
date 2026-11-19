namespace Haruspex.Ir;

/// <summary>
/// Every scalar a document holds, with the JSON path of its node in the document's file: in a parameter, a result, a named type's
/// field and a table type's column, nested ones included. What a version is labelled by when it depends on a kind of value
/// (<see cref="IrDocument.HoldsBytes"/>), and where the reader says a scalar is out of place.
/// </summary>
internal static class IrDocumentScalars
{
    /// <summary>Each scalar of <paramref name="document"/> and its path, in the order of the file: objects, types, then table types.</summary>
    public static IEnumerable<(string Path, IrScalar Scalar)> Of(IrDocument document)
    {
        foreach (var (obj, i) in document.Objects.Select((o, i) => (o, i)))
        {
            foreach (var (parameter, j) in obj.Parameters.Select((p, j) => (p, j)))
            {
                foreach (var scalar in Within(parameter.Type, $"$.objects[{i}].parameters[{j}].type"))
                    yield return scalar;
            }

            foreach (var (result, k) in (obj.Results ?? []).Select((r, k) => (r, k)))
            {
                foreach (var scalar in Within(result.Type, $"$.objects[{i}].results[{k}].type"))
                    yield return scalar;
            }
        }

        foreach (var (name, type) in document.Types.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            foreach (var (field, i) in type.Fields.Select((f, i) => (f, i)))
            {
                foreach (var scalar in Within(field.Type, $"$.types{IrPath.Key(name)}.fields[{i}].type"))
                    yield return scalar;
            }
        }

        foreach (var (key, tableType) in document.TableTypes.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            foreach (var (column, i) in tableType.Columns.Select((c, i) => (c, i)))
            {
                if (column.Type is { } scalar)
                    yield return ($"$.tableTypes{IrPath.Key(key)}.columns[{i}].type", scalar);
            }
        }
    }

    /// <summary>The scalars in one type, without following references, each with its node's path from <paramref name="path"/>.</summary>
    private static IEnumerable<(string Path, IrScalar Scalar)> Within(IrType type, string path) => type switch
    {
        IrScalar scalar => [(path, scalar)],
        IrRef or IrImport or IrUnknownType => [],
        IrArray array => Within(array.Items, path + ".items"),
        IrTuple tuple => tuple.Items.SelectMany((element, i) => Within(element.Type, $"{path}.items[{i}].type")),
        IrRecord record => Within(record.Values, path + ".values"),
        IrUnion union => union.Variants.SelectMany((variant, i) => Within(variant, $"{path}.variants[{i}]")),
        IrJsonText text => text.Content is { } content ? Within(content, path + ".content") : [],
    };
}
