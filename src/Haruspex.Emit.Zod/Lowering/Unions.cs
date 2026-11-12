using Haruspex.Emit.Zod.Model;
using static Haruspex.Emit.Zod.Model.ZodExpression;

namespace Haruspex.Emit.Zod.Lowering;

/// <summary>
/// A union without the variants it doesn't need: a variant that another variant already accepts every value of adds
/// nothing, so it is left out, and a union left with one variant is that variant. <c>z.union([z.string().max(60),
/// z.string().max(13)])</c> is <c>z.string().max(60)</c>, and <c>z.union([z.number(), z.number()])</c> is <c>z.number()</c>.
/// </summary>
/// <remarks>
/// Leaving a variant out changes neither what the schema accepts nor the inferred type (<c>string | "SPARE"</c> is
/// <c>string</c>), so it claims exactly what it did. The test of "accepts every value of" is deliberately narrow: equal
/// variants, a bare <c>z.number()</c> over any number with value checks, and a string with at most a maximum length over a
/// string or an enum whose every value fits. Anything it doesn't recognise is kept, which is longer but never wrong. Objects
/// are never compared: a <c>z.object</c> strips keys it doesn't know, so which variant takes a value matters to what comes
/// out (see <see cref="TypeLowering"/>'s union).
/// </remarks>
internal static class Unions
{
    private static readonly HashSet<string> ValueChecks = new(StringComparer.Ordinal) { "min", "max", "gt", "lt", "multipleOf", "length", "regex" };

    /// <summary>The union of <paramref name="variants"/>, tried in order, without those another variant covers.</summary>
    public static ZodExpression Of(IEnumerable<ZodExpression> variants)
    {
        var all = variants.ToList();
        var kept = all.Where((variant, i) => !Enumerable.Range(0, all.Count).Any(j => j != i && Covers(all[j], variant) && (!Covers(variant, all[j]) || j < i))).ToList();
        return kept.Count == 1 ? kept[0] : new Union([.. kept]);
    }

    /// <summary>Whether every value <paramref name="narrower"/> accepts, <paramref name="wider"/> accepts too.</summary>
    public static bool Covers(ZodExpression wider, ZodExpression narrower)
    {
        if (wider == narrower)
            return true;
        var (wideRoot, wideChecks) = Chain(Bare(wider));
        var (narrowRoot, narrowChecks) = Chain(Bare(narrower));
        if (wideRoot is null || narrowRoot is null || !narrowChecks.All(c => ValueChecks.Contains(c.Method)))
            return false;

        // Any number Zod accepts: every builder below accepts only finite numbers, as z.number() does.
        if (wideRoot is { Method: "number" } && wideChecks.Count == 0)
            return narrowRoot.Method is "number" or "int" or "int32" or "literal";

        if (wideRoot is { Method: "string" } && wideChecks.All(c => c.Method == "max"))
        {
            var limit = wideChecks.Select(Length).DefaultIfEmpty(int.MaxValue).Min();
            return narrowRoot switch
            {
                // JavaScript and .NET both count a string's length in UTF-16 code units, as z.string().max does.
                { Method: "enum", Arguments: [ArrayLiteral items] } => items.Items.All(i => i is Text text && text.Value.Length <= limit),
                { Method: "string" } => narrowChecks.Any(c => c.Method is "max" or "length" && Length(c) <= limit) || limit == int.MaxValue,
                _ => false,
            };
        }

        return false;
    }

    /// <summary>The expression without its notes, which don't change what it accepts.</summary>
    private static ZodExpression Bare(ZodExpression expression) => expression is Noted noted ? Bare(noted.Expression) : expression;

    /// <summary>A chain on <c>z</c>: the builder called on it, and the checks after it; no builder for anything else.</summary>
    private static (Call? Root, List<Call> Checks) Chain(ZodExpression expression)
    {
        var checks = new List<Call>();
        while (expression is Call { Target: not Name } call)
        {
            checks.Insert(0, call);
            expression = call.Target;
        }

        return expression is Call { Target: Name { Path: ZodModule.ZodName } } root ? (root, checks) : (null, checks);
    }

    private static int Length(Call check) =>
        check.Arguments is [Number number] && int.TryParse(number.Value, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : int.MaxValue;
}
