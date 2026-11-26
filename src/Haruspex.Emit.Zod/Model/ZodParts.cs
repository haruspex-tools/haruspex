namespace Haruspex.Emit.Zod.Model;

/// <summary>
/// An expression and every expression inside it, object schemas' properties included: what a module split into files asks of a
/// declaration, to know which bindings and helpers its file must import or define.
/// </summary>
internal static class ZodParts
{
    /// <summary><paramref name="expression"/> and every expression inside it, at any depth.</summary>
    public static IEnumerable<ZodExpression> Within(ZodExpression expression) => Parts(expression).SelectMany(Within).Prepend(expression);

    /// <summary>The bindings <paramref name="expression"/> names: schemas declared in the module, imported, or Zod's own <c>z</c>.</summary>
    public static IEnumerable<string> Names(ZodExpression expression) => Within(expression).OfType<ZodExpression.Name>().Select(n => n.Path);

    private static IEnumerable<ZodExpression> Parts(ZodExpression expression) => expression switch
    {
        ZodExpression.Call call => [call.Target, .. call.Arguments],
        ZodExpression.Union union => union.Variants,
        ZodExpression.ObjectSchema obj => obj.Properties.Select(p => p.Value),
        ZodExpression.JsonText text => [text.Content],
        ZodExpression.Branded branded => [branded.Target],
        ZodExpression.Noted noted => [noted.Expression],
        ZodExpression.ArrayLiteral array => array.Items,
        ZodExpression.ObjectLiteral obj => obj.Properties.Select(p => p.Value),
        ZodExpression.Name or ZodExpression.Number or ZodExpression.Text or ZodExpression.Boolean or ZodExpression.PaddedValues or ZodExpression.PowerOfTen or ZodExpression.Canonical
            or ZodExpression.BigIntLiteral or ZodExpression.Pattern or ZodExpression.TextRange or ZodExpression.UnitsAtMost or ZodExpression.Utf8BytesAtMost or ZodExpression.IntegerTextRange => [],
    };
}
