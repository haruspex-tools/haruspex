namespace Haruspex.Ir;

/// <summary>A column of a table type's definition (0.25).</summary>
/// <param name="Name">Its name as declared.</param>
/// <param name="Sql">Its type as the database spells it, such as <c>nvarchar(200)</c>; null for a computed column, which declares none.</param>
/// <param name="Nullable">Whether a row may hold NULL in it.</param>
public sealed record IrTableColumn(string Name, string? Sql, bool Nullable)
{
    /// <summary>
    /// Its type with the facts its declaration and the type's CHECK constraints prove of a value, as a parameter's type carries
    /// them; null when its type isn't modelled.
    /// </summary>
    public IrScalar? Type { get; init; }

    /// <summary>How the server fills it, when a row can't: an identity, computed or <c>rowversion</c> column; null for one a row gives.</summary>
    public IrColumnGeneration? Generated { get; init; }

    /// <summary>The collation its strings compare under, when it is a string column and its collation is known; null otherwise.</summary>
    public string? Collation { get; init; }
}
