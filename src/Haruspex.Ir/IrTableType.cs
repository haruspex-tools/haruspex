using Haruspex.Collections;

namespace Haruspex.Ir;

/// <summary>
/// A user-defined table type's definition (0.25): what a table-valued parameter of it takes, as the database defines it, so that
/// a document says what its parameters accept without the database at hand, and two documents' parameters can be compared.
/// </summary>
/// <remarks>
/// It is the database's side of the parameter; the parameter's <see cref="IrParameter.Type"/>, an array of a named object type
/// owned by the table type (<see cref="IrTypeOwner.TableType"/>), is what a client sends. The two are kept apart because a column
/// a client can't send (an identity, a computed column) is part of the definition and of no row a client builds.
/// Why: docs/decisions/0076-a-table-valued-parameter-is-an-array-of-strict-rows-of-its-type.md and
/// docs/decisions/0077-the-ir-carries-a-table-type-s-definition.md.
/// </remarks>
/// <param name="Schema">The schema it is defined in, as the database names it.</param>
/// <param name="Name">Its name within its schema, as the database names it.</param>
/// <param name="Columns">Its columns, in the order they are declared, which is the order a client sends them in: the server matches them by place.</param>
/// <param name="At">Where it is defined, as <c>path:line:column</c> relative to the source root.</param>
public sealed record IrTableType(string Schema, string Name, EquatableArray<IrTableColumn> Columns, string At)
{
    /// <summary>Its primary key, unique constraints and unique indexes, each a set of columns no two rows may share, in the order declared.</summary>
    public EquatableArray<IrTableKey> Keys { get; init; } = [];

    /// <summary>
    /// Its CHECK constraints' conditions, each as written on one line, its comments and runs of white space each one space, in the
    /// order declared: a condition the IR can't express as a facet is still one a row must meet.
    /// </summary>
    public EquatableArray<string> Checks { get; init; } = [];

    /// <summary>Its name as the database writes it: the schema and the name, each in brackets (<c>[sales].[line_list]</c>).</summary>
    public string Bracketed => $"[{Schema.Replace("]", "]]", StringComparison.Ordinal)}].[{Name.Replace("]", "]]", StringComparison.Ordinal)}]";

    /// <summary>
    /// Whether it defines what <paramref name="other"/> does: the same name, columns, keys and conditions, wherever each is
    /// defined; a fact of a column's type is compared by what it says, not by where it came from.
    /// </summary>
    /// <param name="other">The other definition.</param>
    public bool Defines(IrTableType other) =>
        other is not null && Unplaced(this) with { At = other.At } == Unplaced(other);

    /// <summary>The definition with every fact's source set aside.</summary>
    private static IrTableType Unplaced(IrTableType type) =>
        type with { Columns = [.. type.Columns.Select(c => c with { Type = c.Type is { Facets: { } facets } scalar ? scalar with { Facets = Unplaced(facets) } : c.Type })] };

    private static IrFacets Unplaced(IrFacets facets)
    {
        var nowhere = new IrFactSource(IrFactSourceKind.Type, "");
        return facets with
        {
            Length = facets.Length is { } length ? length with { From = nowhere } : null,
            MinLength = facets.MinLength is { } minimum ? minimum with { From = nowhere } : null,
            Digits = facets.Digits is { } digits ? digits with { From = nowhere } : null,
            Min = facets.Min is { } min ? min with { From = nowhere } : null,
            Max = facets.Max is { } max ? max with { From = nowhere } : null,
            Values = facets.Values is { } values ? values with { From = [] } : null,
        };
    }

    /// <summary>
    /// Whether <paramref name="key"/> holds of every set of rows that holds this type's keys: some key of this type, refusing
    /// duplicates as <paramref name="key"/> does or more, has no column <paramref name="key"/> lacks. Rows unique on fewer columns
    /// are unique on more.
    /// </summary>
    /// <param name="key">The key, of another definition of the type.</param>
    public bool Implies(IrTableKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Keys.Any(k => k.Columns.All(c => key.Columns.Contains(c, StringComparer.OrdinalIgnoreCase)) && (key.IgnoresDuplicates || !k.IgnoresDuplicates));
    }
}
