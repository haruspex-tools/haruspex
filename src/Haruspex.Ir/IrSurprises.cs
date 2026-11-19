namespace Haruspex.Ir;

/// <summary>
/// Which fields are surprises: a field whose nullability, presence or type differs from what its source column declares, and
/// every unknown. What generated code comments by default, and what <c>haruspex explain</c> lists for an object, so that a
/// deliberate widening (a <c>COUNT</c> that may be null, a key left out when null) is said where it is read rather than found as
/// a bug (<c>docs/decisions/0079-a-surprise-is-explained-where-it-is-written.md</c>).
/// </summary>
public static class IrSurprises
{
    /// <summary>
    /// Whether a null reason is one a reader expects from the declarations alone: a nullable column the field reads, a parameter
    /// (which a caller may always pass as null) and a nullability the config file declared. Every other reason is a rule that
    /// acted on the value on its way to the result.
    /// </summary>
    public static bool IsDeclared(IrNullRule rule) => rule is IrNullRule.NullableColumn or IrNullRule.Parameter or IrNullRule.Declared;

    /// <summary>Whether <paramref name="field"/> is a surprise, as the summary says.</summary>
    public static bool Of(IrField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return Reasons(field).Count > 0 || AbsentWhenNull(field) || TypeDiffers(field) || field.Type is IrUnknownType;
    }

    /// <summary>The reasons a field may be null that its declarations don't give, in the order the IR lists them.</summary>
    public static IReadOnlyList<IrNullReason> Reasons(IrField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return [.. field.NullableBecause.Where(r => !IsDeclared(r.Rule))];
    }

    /// <summary>Whether the field's key may be missing: left out when null, while null, or left out whatever its value.</summary>
    public static bool AbsentWhenNull(IrField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return field.Presence == IrPresence.Optional || (field.Presence == IrPresence.OmittedWhenNull && field.Nullable);
    }

    /// <summary>
    /// Whether the field is a scalar of a type none of its source columns has: an aggregate, arithmetic or a set operation made
    /// it another type (<c>SUM</c> of a <c>decimal(9,2)</c> is a <c>decimal(38,2)</c>). False for a field that reads no column.
    /// </summary>
    public static bool TypeDiffers(IrField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return field.Type is IrScalar { Sql: { } sql } && field.From.Count > 0
            && field.From.Where(c => c.Sql is not null).ToList() is { Count: > 0 } typed
            && typed.All(c => !string.Equals(c.Sql, sql, StringComparison.Ordinal));
    }
}
