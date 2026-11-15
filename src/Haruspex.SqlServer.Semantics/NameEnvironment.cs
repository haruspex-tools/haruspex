using System.Collections.Immutable;
using Haruspex.Collections;

namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// The names a body has defined at one statement: <c>inserted</c> and <c>deleted</c> in an <c>OUTPUT</c>, the CTEs of the
/// statement's <c>WITH</c>, what the body declares (its parameters, variables and table variables), and the temporary tables
/// with the columns the walk knows them to have there. Immutable, and compared by value, so that a statement
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
    private readonly ImmutableDictionary<LocalName, TableVariable> tableVariables;
    private readonly ImmutableDictionary<LocalName, EquatableArray<string>> temporaryColumns;

    private NameEnvironment(
        ImmutableDictionary<string, PseudoTable> pseudo,
        ImmutableDictionary<string, CommonTable> ctes,
        ImmutableDictionary<LocalName, TableVariable> tableVariables,
        ImmutableDictionary<LocalName, EquatableArray<string>> temporaryColumns)
    {
        this.pseudo = pseudo;
        this.ctes = ctes;
        this.tableVariables = tableVariables;
        this.temporaryColumns = temporaryColumns;
    }

    /// <summary>No relations of the body's own.</summary>
    public static NameEnvironment Empty { get; } = new(
        ImmutableDictionary.Create<string, PseudoTable>(Names),
        ImmutableDictionary.Create<string, CommonTable>(Names),
        [],
        []);

    /// <summary>What the body declares for the whole of it: its parameters, variables, table variables and cursors.</summary>
    public BodyDeclarations Declarations { get; private init; } = BodyDeclarations.None;

    /// <summary>These names with what the body declares; its table variables are names in FROM.</summary>
    /// <param name="declarations">The body's declarations.</param>
    public NameEnvironment WithDeclarations(BodyDeclarations declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        var table = ImmutableDictionary<LocalName, TableVariable>.Empty;
        foreach (var declaration in declarations.TableVariables)
            table = table.SetItem(new LocalName(declaration.Name), new TableVariable(declaration));
        return new(pseudo, ctes, table, temporaryColumns) { Declarations = declarations };
    }

    /// <summary>
    /// These names with the temporary tables' columns as the walk knows them at a statement, replacing any given before; a
    /// temporary table not listed has columns not known (the caller's, or one whose columns a path made unknown).
    /// </summary>
    /// <param name="columns">Each temporary table whose columns are known, by name, with its columns in order.</param>
    public NameEnvironment WithTemporaryColumns(IEnumerable<KeyValuePair<LocalName, IReadOnlyList<string>>> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        return new(pseudo, ctes, tableVariables, ImmutableDictionary.CreateRange(columns.Select(c => KeyValuePair.Create(c.Key, (EquatableArray<string>)[.. c.Value])))) { Declarations = Declarations };
    }

    /// <summary>These names with a CTE added, hiding any table of its name.</summary>
    /// <param name="cte">The CTE.</param>
    public NameEnvironment WithCte(CommonTable cte)
    {
        ArgumentNullException.ThrowIfNull(cte);
        return new(pseudo, ctes.SetItem(cte.Name, cte), tableVariables, temporaryColumns) { Declarations = Declarations };
    }

    /// <summary>These names with <c>inserted</c> or <c>deleted</c> added, for a write's <c>OUTPUT</c>.</summary>
    /// <param name="table">The pseudo-table.</param>
    public NameEnvironment WithPseudo(PseudoTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return new(pseudo.SetItem(table.Name, table), ctes, tableVariables, temporaryColumns) { Declarations = Declarations };
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
        var local = new LocalName(name);
        if (local.IsVariable)
            return tableVariables.TryGetValue(local, out var variable) ? variable : null;
        if (local.IsTemporary)
            return new TemporaryTable(name, temporaryColumns.TryGetValue(local, out var columns) ? columns : null);
        return null;
    }

    /// <summary>The table variable of this name, as declared; null when the body declares none.</summary>
    /// <param name="name">The name, with its <c>@</c>.</param>
    public TableVariable? TableVariable(LocalName name) => tableVariables.TryGetValue(name, out var variable) ? variable : null;

    /// <inheritdoc/>
    public bool Equals(NameEnvironment? other) =>
        other is not null
        && (ReferenceEquals(this, other)
            || (ReferenceEquals(Declarations, other.Declarations)
                && Same(pseudo, other.pseudo, (a, b) => a.Name == b.Name && Same(a.Columns, b.Columns) && ReferenceEquals(a.Written, b.Written))
                && Same(ctes, other.ctes, ReferenceEquals)
                && Same(tableVariables, other.tableVariables, ReferenceEquals)
                && Same(temporaryColumns, other.temporaryColumns, Same)));

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as NameEnvironment);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(pseudo.Count);
        hash.Add(ctes.Count);
        hash.Add(tableVariables.Count);
        var temporary = 0;
        foreach (var name in temporaryColumns.Keys)
            temporary = unchecked(temporary + name.GetHashCode());
        hash.Add(temporary);
        return hash.ToHashCode();
    }

    private static bool Same<TKey, T>(ImmutableDictionary<TKey, T> a, ImmutableDictionary<TKey, T> b, Func<T, T, bool> same)
        where TKey : notnull =>
        a.Count == b.Count && a.All(entry => b.TryGetValue(entry.Key, out var other) && same(entry.Value, other));

    private static bool Same(IReadOnlyList<string>? a, IReadOnlyList<string>? b) =>
        ReferenceEquals(a, b) || (a is not null && b is not null && a.SequenceEqual(b, StringComparer.Ordinal));
}
