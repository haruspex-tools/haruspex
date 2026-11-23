namespace Haruspex.Ir;

/// <summary>
/// Every type node a document holds, with the JSON path of the node in the document's file: in a parameter, a result, a named type's
/// field and a table type's column, nested ones included. What a version is labelled by when it depends on a kind of value
/// (<see cref="IrDocument.HoldsBytes"/>, <see cref="IrDocument.Declares"/>), and where the reader says a node is out of place.
/// </summary>
internal static class IrDocumentScalars
{
    /// <summary>Each scalar of <paramref name="document"/> and its path, in the order of the file: objects, types, then table types.</summary>
    public static IEnumerable<(string Path, IrScalar Scalar)> Of(IrDocument document) =>
        Nodes(document).Where(n => n.Node is IrScalar).Select(n => (n.Path, (IrScalar)n.Node));

    /// <summary>Each type node of <paramref name="document"/> but references, and its path, in the order of the file.</summary>
    public static IEnumerable<(string Path, IrType Node)> Nodes(IrDocument document)
    {
        foreach (var (obj, i) in document.Objects.Select((o, i) => (o, i)))
        {
            foreach (var (parameter, j) in obj.Parameters.Select((p, j) => (p, j)))
            {
                foreach (var node in Within(parameter.Type, $"$.objects[{i}].parameters[{j}].type"))
                    yield return node;
            }

            foreach (var (result, k) in (obj.Results ?? []).Select((r, k) => (r, k)))
            {
                foreach (var node in Within(result.Type, $"$.objects[{i}].results[{k}].type"))
                    yield return node;
            }
        }

        foreach (var (name, type) in document.Types.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            foreach (var (field, i) in type.Fields.Select((f, i) => (f, i)))
            {
                foreach (var node in Within(field.Type, $"$.types{IrPath.Key(name)}.fields[{i}].type"))
                    yield return node;
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

    /// <summary>
    /// The nodes of one type, itself first, without following references, each with its path from <paramref name="path"/>.
    /// Which node holds which is <see cref="IrTypeTree.Children"/>'s to say, so that this walk can't miss a kind of node; only
    /// how the file names each step is said here.
    /// </summary>
    private static IEnumerable<(string Path, IrType Node)> Within(IrType type, string path) =>
        type is IrRef ? [] : [(path, type), .. IrTypeTree.Children(type).SelectMany((child, i) => Within(child.Type, path + Step(type, i)))];

    /// <summary>The step of the file's JSON path from <paramref name="type"/> to its <paramref name="i"/>th child.</summary>
    private static string Step(IrType type, int i) => type switch
    {
        IrArray => ".items",
        IrTuple => $".items[{i}].type",
        IrRecord => ".values",
        IrUnion => $".variants[{i}]",
        IrJsonText => ".content",
        _ => throw new InvalidOperationException($"{type.GetType().Name} holds no type, so it has no step to one."),
    };
}
