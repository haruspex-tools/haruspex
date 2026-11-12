namespace Haruspex.Emit.Zod.Model;

/// <summary>The notes an expression carries for its reader, gathered from every <see cref="ZodExpression.Noted"/> inside it.</summary>
internal static class ZodNotes
{
    /// <summary>
    /// The notes of <paramref name="expression"/> as one text, or null when it has none. A note comes first, followed by the
    /// notes of what it is about (<c>JSON text; …</c>); the notes of an expression's parts follow in order, each distinct text
    /// once, so a tuple of two strings of the same type says its caveat once.
    /// </summary>
    public static string? Of(ZodExpression expression) => expression switch
    {
        ZodExpression.Noted noted => Of(noted.Expression) is { } inner ? noted.Note + "; " + inner : noted.Note,
        _ => Join(Parts(expression).Select(Of)),
    };

    private static IEnumerable<ZodExpression> Parts(ZodExpression expression) => expression switch
    {
        ZodExpression.Call call => [call.Target, .. call.Arguments],
        ZodExpression.Union union => union.Variants,
        ZodExpression.JsonText text => [text.Content],
        ZodExpression.ArrayLiteral array => array.Items,
        ZodExpression.ObjectLiteral obj => obj.Properties.Select(p => p.Value),

        // An object schema's properties each carry their own notes, written beside the property.
        ZodExpression.ObjectSchema => [],
        ZodExpression.Name or ZodExpression.Number or ZodExpression.Text or ZodExpression.Boolean or ZodExpression.PaddedValues or ZodExpression.PowerOfTen => [],
        _ => throw new ArgumentOutOfRangeException(nameof(expression), expression, "An expression outside the model's closed set."),
    };

    private static string? Join(IEnumerable<string?> notes)
    {
        var distinct = notes.OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        return distinct.Count == 0 ? null : string.Join("; ", distinct);
    }
}
