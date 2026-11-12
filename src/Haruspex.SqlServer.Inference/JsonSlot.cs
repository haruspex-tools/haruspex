using Haruspex.SqlServer.Inference.Lattice;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// What a place the body writes holds as JSON: a variable of type <c>nvarchar(max)</c>, or such a declared column of
/// a temporary table or table variable. Built JSON keeps its content there, and <c>json_query</c> over the place makes it
/// live again (observed: Json_built_here_keeps_its_content_through_variables_and_tables_until_json_query). A place of a
/// shorter type cuts the text short without an error (observed), so it is not traced.
/// </summary>
/// <remarks>
/// Writes join: a variable assigned on several paths, or by an assignment that may not happen (<c>SELECT @x = … FROM</c> over
/// no rows keeps the earlier value, observed), and a column written by several statements, hold the content of any of them.
/// A NULL written adds nothing. A write of a value that may not be JSON (a parameter, a stored column, concatenation, a literal
/// that is not a JSON document) makes the content unknown, and <see cref="NotJson"/> names that write.
/// </remarks>
/// <param name="Content">The JSON every non-null value written is, when one has been; null when only NULL has been written.</param>
/// <param name="NotJson">Why a value written may not be JSON built here, naming the write; null when every one is.</param>
internal sealed record JsonSlot(Shape? Content, UnknownShape? NotJson)
{
    /// <summary>Nothing but NULL written yet: a variable before its first assignment, a table before its first row.</summary>
    public static JsonSlot Empty { get; } = new(null, null);

    public static JsonSlot Json(Shape content) => new(content, null);

    public static JsonSlot Text(UnknownShape why) => new(null, why);

    /// <summary>A place written by either of two groups of writes.</summary>
    public static JsonSlot Join(JsonSlot a, JsonSlot b)
    {
        if (a.NotJson is not null)
            return a;
        if (b.NotJson is not null)
            return b;
        if (a.Content is null)
            return b;
        if (b.Content is null)
            return a;
        return Json(JsonShapes.Join(a.Content, b.Content));
    }

    /// <summary>
    /// Whether two slots say the same: both not JSON (whichever write is named, a witness), or both the same content up to
    /// witnesses (<see cref="JsonShapes.Same"/>), or both empty.
    /// </summary>
    public static bool Same(JsonSlot a, JsonSlot b) =>
        (a.NotJson is null) == (b.NotJson is null)
        && (a.NotJson is not null || (a.Content is null ? b.Content is null : b.Content is not null && JsonShapes.Same(a.Content, b.Content)));

    /// <summary>
    /// Whether every write <paramref name="a"/> allows, <paramref name="b"/> allows: anything is below a slot that may not
    /// hold JSON, nothing written is below anything, and JSON content is ordered by <see cref="JsonShapes.Below"/>.
    /// </summary>
    public static bool Below(JsonSlot a, JsonSlot b) =>
        b.NotJson is not null || (a.NotJson is null && (a.Content is null || (b.Content is not null && JsonShapes.Below(a.Content, b.Content))));

    /// <summary>
    /// What a read of the place gives, its declared value being <paramref name="declared"/>: JSON text of the content, null as
    /// the declared value is; or the declared value, carrying why its JSON is not known for <c>json_query</c> to report.
    /// </summary>
    public Value Read(Value declared)
    {
        if (NotJson is { } why)
            return declared with { JsonWhy = why };
        return Content is { } content ? new Value(new JsonTextShape(content), declared.NullableBecause) : declared;
    }
}
