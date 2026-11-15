namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// The full name a source without an alias is known by in its query, which a column or star of three parts names it by
/// (<c>shop.orders.order_id</c>, <c>shop.orders.*</c>): its schema and name in this database. A name written with its schema
/// is exposed so; a one-part name under the schema it resolves to for the caller (observed:
/// A_one_part_name_is_exposed_under_the_schema_it_resolves_to); a temporary table under any schema (observed:
/// A_temporary_tables_full_name_takes_any_schema).
/// </summary>
/// <param name="Schema">The schema; null for a temporary table, which a full name with any schema names.</param>
/// <param name="Name">The table's, view's, synonym's or function's name.</param>
public sealed record ExposedName(string? Schema, string Name)
{
    /// <summary>Whether the full name <paramref name="schema"/>.<paramref name="name"/> names it, ignoring case.</summary>
    /// <param name="schema">The schema part as written.</param>
    /// <param name="name">The name part as written.</param>
    public bool IsNamed(string schema, string name) =>
        (Schema is null || Schema.Equals(schema, StringComparison.OrdinalIgnoreCase)) && Name.Equals(name, StringComparison.OrdinalIgnoreCase);
}
