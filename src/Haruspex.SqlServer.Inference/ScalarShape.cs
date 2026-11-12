using Haruspex.Ir;
using Haruspex.SqlServer.Semantics;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// A scalar value: what the IR says of it, and the SQL Server type the server's rules read, carried beside it so that no
/// rule reads a type back from the IR's text.
/// </summary>
/// <param name="Type">The scalar as the IR writes it, its SQL type as written in <see cref="IrScalar.Sql"/>.</param>
/// <param name="BuiltIn">
/// Its built-in type with its arguments, when inference models it; null for a scalar with no SQL type (a JSON literal's) and
/// for one read from the IR whose text names none (<see cref="FromIr"/>).
/// </param>
internal sealed record ScalarShape(IrScalar Type, ScalarType? BuiltIn) : Shape
{
    /// <summary>
    /// A scalar read from the IR, such as a declared result's: the one place inference reads a type from text, at the boundary
    /// where the IR comes in.
    /// </summary>
    public static ScalarShape FromIr(IrScalar type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return new ScalarShape(type, SqlTypes.Parse(type.Sql));
    }

    /// <summary>
    /// Whether two scalars are of one type: the same kind and format, and the same built-in type (<c>decimal</c> and
    /// <c>decimal(18,0)</c> are one). Two with no built-in type are one only when the IR writes the same text for them, or none.
    /// </summary>
    public bool SameType(ScalarShape other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Type.Kind == other.Type.Kind && Type.Format == other.Type.Format
            && BuiltIn == other.BuiltIn && (BuiltIn is not null || Type.Sql == other.Type.Sql);
    }

    /// <summary>
    /// The scalar a value of either type has (they are <see cref="SameType"/>), with <paramref name="facets"/>: written as both
    /// write it, or, where one was declared without its arguments (<c>decimal</c>) and the other with them, as the server
    /// describes the type (<c>decimal(18,0)</c>).
    /// </summary>
    public ScalarShape Joined(ScalarShape other, IrFacets? facets)
    {
        ArgumentNullException.ThrowIfNull(other);
        var sql = Type.Sql == other.Type.Sql || BuiltIn is null ? Type.Sql : BuiltIn.Text;
        return this with { Type = Type with { Sql = sql, Facets = facets } };
    }

    /// <summary>The same scalar with other facets, its type unchanged.</summary>
    public ScalarShape WithFacets(IrFacets? facets) => this with { Type = Type with { Facets = facets } };
}
