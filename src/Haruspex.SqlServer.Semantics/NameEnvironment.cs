using System.Collections.Immutable;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// The relations a body has defined that a one-part name in FROM may mean at one statement: <c>inserted</c> and
/// <c>deleted</c> in an <c>OUTPUT</c>, the CTEs of the statement's <c>WITH</c>, the table variables as declared, and the
/// temporary tables with the columns the walk knows them to have there. Immutable, and compared by value, so that a statement
/// read again with the same names (on a loop's next pass, on another branch) is bound once (<see cref="Binder"/>).
/// </summary>
/// <remarks>
/// A name is looked for in that order: <c>inserted</c> and <c>deleted</c>, then a CTE, which hides a table of its name until
/// its statement ends; then <c>@t</c> among the table variables and <c>#t</c> among the temporary tables. Any other name falls
/// through to the schema's objects.
/// </remarks>
public sealed class NameEnvironment : IEquatable<NameEnvironment>
{
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    private readonly ImmutableDictionary<string, PseudoTable> pseudo;
    private readonly ImmutableDictionary<string, CommonTable> ctes;
    private readonly ImmutableDictionary<string, TableVariable> tableVariables;
    private readonly ImmutableDictionary<string, IReadOnlyList<string>> temporaryColumns;

    private NameEnvironment(
        bool expandsStars,
        ImmutableDictionary<string, PseudoTable> pseudo,
        ImmutableDictionary<string, CommonTable> ctes,
        ImmutableDictionary<string, TableVariable> tableVariables,
        ImmutableDictionary<string, IReadOnlyList<string>> temporaryColumns)
    {
        ExpandsStars = expandsStars;
        this.pseudo = pseudo;
        this.ctes = ctes;
        this.tableVariables = tableVariables;
        this.temporaryColumns = temporaryColumns;
    }

    /// <summary>No relations of the body's own, in a body whose stars are not expanded: a procedure's, a function's.</summary>
    public static NameEnvironment Empty { get; } = new(
        expandsStars: false,
        ImmutableDictionary.Create<string, PseudoTable>(Names),
        ImmutableDictionary.Create<string, CommonTable>(Names),
        ImmutableDictionary.Create<string, TableVariable>(Names),
        ImmutableDictionary.Create<string, IReadOnlyList<string>>(Names));

    /// <summary>
    /// No relations of the body's own, in a view's body, whose <c>SELECT *</c> SQL Server binds when it creates the view (observed:
    /// A_view_binds_select_star_when_it_is_created), so a star stands for the columns its sources have.
    /// </summary>
    public static NameEnvironment View { get; } = new(true, Empty.pseudo, Empty.ctes, Empty.tableVariables, Empty.temporaryColumns);

    /// <summary>Whether <c>SELECT *</c> is expanded to its sources' columns: only in a view.</summary>
    public bool ExpandsStars { get; }

    /// <summary>These names with the body's table variables, each as declared.</summary>
    /// <param name="declarations">Each table variable's declaration (a table named by the variable, with no schema).</param>
    public NameEnvironment WithTableVariables(IEnumerable<Table> declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        var table = tableVariables;
        foreach (var declaration in declarations)
        {
            if (!table.ContainsKey(declaration.Name.Name))
                table = table.Add(declaration.Name.Name, new TableVariable(declaration));
        }

        return new(ExpandsStars, pseudo, ctes, table, temporaryColumns);
    }

    /// <summary>
    /// These names with the temporary tables' columns as the walk knows them at a statement, replacing any given before; a
    /// temporary table not listed has columns not known (the caller's, or one whose columns a path made unknown).
    /// </summary>
    /// <param name="columns">Each temporary table whose columns are known, by name, with its columns in order.</param>
    public NameEnvironment WithTemporaryColumns(IEnumerable<KeyValuePair<string, IReadOnlyList<string>>> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        return new(ExpandsStars, pseudo, ctes, tableVariables, ImmutableDictionary.CreateRange(Names, columns));
    }

    /// <summary>These names with a CTE added, hiding any table of its name.</summary>
    /// <param name="cte">The CTE.</param>
    public NameEnvironment WithCte(CommonTable cte)
    {
        ArgumentNullException.ThrowIfNull(cte);
        return new(ExpandsStars, pseudo, ctes.SetItem(cte.Name, cte), tableVariables, temporaryColumns);
    }

    /// <summary>These names with <c>inserted</c> or <c>deleted</c> added, for a write's <c>OUTPUT</c>.</summary>
    /// <param name="table">The pseudo-table.</param>
    public NameEnvironment WithPseudo(PseudoTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return new(ExpandsStars, pseudo.SetItem(table.Name, table), ctes, tableVariables, temporaryColumns);
    }

    /// <summary>
    /// The relation a one-part name in FROM means here; null for a name the body defines no relation of, which then means an
    /// object of the schema, or for a table variable the body doesn't declare.
    /// </summary>
    /// <param name="name">The name as written, with its sigil.</param>
    public RelationSymbol? Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (pseudo.TryGetValue(name, out var table))
            return table;
        if (ctes.TryGetValue(name, out var cte))
            return cte;
        if (name.StartsWith('@'))
            return tableVariables.TryGetValue(name, out var variable) ? variable : null;
        if (name.StartsWith('#'))
            return new TemporaryTable(name, temporaryColumns.TryGetValue(name, out var columns) ? columns : null);
        return null;
    }

    /// <summary>The table variable of this name, as declared; null when the body declares none.</summary>
    /// <param name="name">The name, with its <c>@</c>.</param>
    public TableVariable? TableVariable(string name) => tableVariables.TryGetValue(name, out var variable) ? variable : null;

    /// <inheritdoc/>
    public bool Equals(NameEnvironment? other) =>
        other is not null
        && (ReferenceEquals(this, other)
            || (ExpandsStars == other.ExpandsStars
                && Same(pseudo, other.pseudo, (a, b) => a.Name == b.Name && Same(a.Columns, b.Columns))
                && Same(ctes, other.ctes, ReferenceEquals)
                && Same(tableVariables, other.tableVariables, ReferenceEquals)
                && Same(temporaryColumns, other.temporaryColumns, Same)));

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as NameEnvironment);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ExpandsStars);
        hash.Add(pseudo.Count);
        hash.Add(ctes.Count);
        hash.Add(tableVariables.Count);
        foreach (var name in temporaryColumns.Keys.Order(StringComparer.OrdinalIgnoreCase))
            hash.Add(name, Names);
        return hash.ToHashCode();
    }

    private static bool Same<T>(ImmutableDictionary<string, T> a, ImmutableDictionary<string, T> b, Func<T, T, bool> same) =>
        a.Count == b.Count && a.All(entry => b.TryGetValue(entry.Key, out var other) && same(entry.Value, other));

    private static bool Same(IReadOnlyList<string>? a, IReadOnlyList<string>? b) =>
        ReferenceEquals(a, b) || (a is not null && b is not null && a.SequenceEqual(b, StringComparer.Ordinal));
}
