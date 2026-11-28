namespace Haruspex.Emit.Zod.Model;

/// <summary>The notes an expression carries for its reader, gathered from every <see cref="ZodExpression.Noted"/> inside it.</summary>
internal static class ZodNotes
{
    /// <summary>
    /// The notes of <paramref name="expression"/>, in order: a note before the notes of what it is about, and the notes of an
    /// expression's parts in order, each once, so a tuple of two strings of the same type says its note once.
    /// </summary>
    public static IReadOnlyList<ZodNote> Of(ZodExpression expression) => [.. All(expression).Distinct()];

    private static IEnumerable<ZodNote> All(ZodExpression expression) => expression switch
    {
        ZodExpression.Noted noted => [noted.Note, .. All(noted.Expression)],
        _ => Parts(expression).SelectMany(All),
    };

    private static IEnumerable<ZodExpression> Parts(ZodExpression expression) => expression switch
    {
        ZodExpression.Call call => [call.Target, .. call.Arguments],
        ZodExpression.Union union => union.Variants,
        ZodExpression.JsonText text => [text.Content],
        ZodExpression.JsonSent sent => [sent.Content, sent.Fits],
        ZodExpression.Branded branded => [branded.Target],
        ZodExpression.ArrayLiteral array => array.Items,
        ZodExpression.ObjectLiteral obj => obj.Properties.Select(p => p.Value),

        // An object schema's properties each carry their own notes, written above the property.
        ZodExpression.ObjectSchema => [],
        ZodExpression.Name or ZodExpression.Number or ZodExpression.Text or ZodExpression.Boolean or ZodExpression.PaddedValues or ZodExpression.PowerOfTen or ZodExpression.Canonical
            or ZodExpression.BigIntLiteral or ZodExpression.Pattern or ZodExpression.TextRange or ZodExpression.UnitsAtMost or ZodExpression.Utf8BytesAtMost or ZodExpression.IntegerTextRange => [],
        _ => throw new ArgumentOutOfRangeException(nameof(expression), expression, "An expression outside the model's closed set."),
    };
}
