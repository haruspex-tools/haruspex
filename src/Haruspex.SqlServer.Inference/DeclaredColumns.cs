using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// The columns declared to hold JSON of a schema, as shapes, and which bodies read them: each body that reads one directly, and
/// each body a body reaches (a function, a view, a table-valued function, a procedure it calls), so that an object's claims list
/// every declaration its results may rest on, however deep the read (docs/decisions/0093-a-column-s-json-is-part-of-its-type-completed-by-the-config.md).
/// </summary>
/// <remarks>
/// A summary is worked out once per document and then reused, so what a body reads is recorded by the body, and what an
/// object rests on is found afterwards by following what each body reached, rather than by the first walk that met it.
/// </remarks>
internal sealed class DeclaredColumns
{
    private readonly Dictionary<Column, (DeclaredColumn Declared, Shape? Content)> byColumn = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<SourceSpan, SortedSet<string>> read = [];
    private readonly Dictionary<SourceSpan, HashSet<SourceSpan>> reached = [];

    /// <param name="model">The model the columns are looked up in: a declaration of a column it doesn't have is never read.</param>
    /// <param name="columns">The declarations.</param>
    public DeclaredColumns(DatabaseModel model, IReadOnlyList<DeclaredColumn> columns)
    {
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
        if (!read.TryGetValue(reader, out var names))
            read[reader] = names = new(StringComparer.Ordinal);
        names.Add(name);
        if (found.Content is null)
        {
            found = found with { Content = IrShapes.ToShape(found.Declared.Content, found.Declared.Types, $"The JSON {name} is declared to hold", column.At) };
            byColumn[column] = found;
        }

        return found.Content;
    }

    /// <summary>Records that the body <paramref name="from"/> reads or calls the body <paramref name="to"/>.</summary>
    public void Reached(SourceSpan from, SourceSpan to)
    {
        if (!reached.TryGetValue(from, out var targets))
            reached[from] = targets = [];
        targets.Add(to);
    }

    /// <summary>The declared columns the body <paramref name="top"/> reads, itself or through any body it reaches, ordinal order.</summary>
    public IReadOnlyList<string> ReadFrom(SourceSpan top)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<SourceSpan>();
        var pending = new Stack<SourceSpan>([top]);
        while (pending.TryPop(out var body))
        {
            if (!seen.Add(body))
                continue;
            names.UnionWith(read.GetValueOrDefault(body) ?? []);
            foreach (var next in reached.GetValueOrDefault(body) ?? [])
                pending.Push(next);
        }

        return [.. names];
    }
}
