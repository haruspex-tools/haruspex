using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// The columns declared to hold JSON of a schema, as shapes, each read recorded as its reader's (<see cref="DeclarationReads"/>), so
/// that an object's claims list every declaration its results may rest on
/// (docs/decisions/0093-a-column-s-json-is-part-of-its-type-completed-by-the-config.md).
/// </summary>
/// <remarks>
/// A <c>varchar</c> or <c>char</c> column's text is in its collation's code page, so a character the code page lacks, written as
/// itself, comes back as another: its JSON is read as <see cref="CodePageText"/> says that collation gives it back
/// (docs/decisions/0145-varchar-json-is-read-through-its-code-page.md). Only the reads change; the declaration recorded is the schema's.
/// </remarks>
internal sealed class DeclaredColumns
{
    private readonly Dictionary<Column, (DeclaredColumn Declared, string? Collation, bool CodePage, Shape? Content)> byColumn = new(ReferenceEqualityComparer.Instance);
    private readonly DeclarationReads reads;

    /// <param name="model">The model the columns are looked up in: a declaration of a column it doesn't have is never read.</param>
    /// <param name="columns">The declarations.</param>
    /// <param name="reads">Where each read is recorded.</param>
    public DeclaredColumns(DatabaseModel model, IReadOnlyList<DeclaredColumn> columns, DeclarationReads reads)
    {
        this.reads = reads;
        foreach (var declared in columns)
        {
            if (model.FindTable(declared.Table) is { } table && table.Column(declared.Column) is { } column)
            {
                var codePage = SchemaRules.ResolveType(model, column.Type)?.Type is { } type && type.IsAnyOf(BuiltInType.VarChar, BuiltInType.Char);
                byColumn.TryAdd(column, (declared, SchemaRules.EffectiveCollation(model, table, column), codePage, null));
            }
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
            var (content, types) = found.CodePage ? CodePageText.Read(found.Declared.Content, found.Declared.Types, found.Collation) : (found.Declared.Content, found.Declared.Types);
            found = found with { Content = IrShapes.ToShape(content, types, $"The JSON {name} is declared to hold", column.At) };
            byColumn[column] = found;
        }

        return found.Content;
    }
}
