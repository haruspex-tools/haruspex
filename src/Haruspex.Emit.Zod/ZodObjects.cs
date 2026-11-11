namespace Haruspex.Emit.Zod;

/// <summary>How result objects treat keys their type does not list.</summary>
public enum ZodObjects
{
    /// <summary><c>z.object</c>: unknown keys are removed when parsing. The default.</summary>
    Strip,

    /// <summary><c>z.strictObject</c>: an unknown key fails parsing.</summary>
    Strict,
}
