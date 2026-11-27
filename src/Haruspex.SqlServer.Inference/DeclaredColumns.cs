using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// The columns declared to hold JSON of a schema, as shapes, each read recorded as its reader's (<see cref="DeclarationReads"/>), so
/// that an object's claims list every declaration its results may rest on
/// (docs/decisions/0093-a-column-s-json-is-part-of-its-type-completed-by-the-config.md).
/// </summary>
internal sealed class DeclaredColumns
{
    private readonly Dictionary<Column, (DeclaredColumn Declared, Shape? Content)> byColumn = new(ReferenceEqualityComparer.Instance);
    private readonly DeclarationReads reads;

    /// <param name="model">The model the columns are looked up in: a declaration of a column it doesn't have is never read.</param>
    /// <param name="columns">The declarations.</param>
    /// <param name="reads">Where each read is recorded.</param>
    public DeclaredColumns(DatabaseModel model, IReadOnlyList<DeclaredColumn> columns, DeclarationReads reads)
    {
        this.reads = reads;
        foreach (var declared in columns)
        {
            if (model.FindTable(declared.Table)?.Column(declared.Column) is { } column)
                byColumn.TryAdd(column, (declared, null));
        }
    }

    /// <summary>
    /// What a non-null value of <paramref name="column"/> parses to, when it is declared; null when it isn't. The read is recorded
    /// as <paramref name="reader"/>'s, the body whose query reads it.
    /// </summary>
    public Shape? Content(Column column, SourceSpan reader)
    {
        if (!byColumn.TryGetValue(column, out var found))
            return null;
        var name = found.Declared.Record.Column;
        reads.Read(reader, IrOverrideSetting.ColumnJson, name);
        if (found.Content is null)
        {
            found = found with { Content = IrShapes.ToShape(found.Declared.Content, found.Declared.Types, $"The JSON {name} is declared to hold", column.At) };
            byColumn[column] = found;
        }

        return found.Content;
    }
}
