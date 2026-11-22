using Haruspex.Collections;

namespace Haruspex.Ir;

/// <summary>The folds a document's canonical value sets name (<see cref="IrDocument.Folds"/>), and the table that holds exactly them.</summary>
public static class IrDocumentFolds
{
    /// <summary>
    /// The names of the folds the document's value sets name, wherever a type stands: the parameters, the results, the type table
    /// and the table types' columns. A table type with an identity, computed or rowversion column has no row type, so its
    /// column is the only place its set stands; the reader refuses a file whose fold table leaves any of these out.
    /// </summary>
    public static IReadOnlySet<string> Named(IrDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return IrDocumentScalars.Of(document)
            .Select(s => s.Scalar.Facets?.Values?.Fold)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// <paramref name="document"/> with the folds its value sets name and no others, each taken from its own table or else from
    /// <paramref name="available"/>: what a step that changes sets (an override that makes one exact, results declared in the
    /// config file) leaves named.
    /// </summary>
    /// <exception cref="InvalidOperationException">A set names a fold none of the tables holds: a fault of the step that made it.</exception>
    public static IrDocument Settled(IrDocument document, IEnumerable<EquatableMap<string, IrFold>> available)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(available);
        var tables = available.Prepend(document.Folds).ToList();
        return document with
        {
            Folds = Named(document).ToEquatableMap(
                name => name,
                name => tables.FirstOrDefault(t => t.ContainsKey(name))?[name] ?? throw new InvalidOperationException($"A value set names the fold {name}, which no table holds.")),
        };
    }
}
