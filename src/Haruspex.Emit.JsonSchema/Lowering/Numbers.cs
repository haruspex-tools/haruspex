using System.Globalization;
using Haruspex.Emit.JsonSchema.Model;
using Haruspex.Ir;

namespace Haruspex.Emit.JsonSchema.Lowering;

/// <summary>
/// A number with every fact the IR carries about it, as JSON Schema keywords: <c>integer</c> where every value is whole, its
/// bounds as <c>minimum</c> and <c>maximum</c>, its exact value set as <c>enum</c>. What a validator can't check exactly is
/// widened, and a note says how.
/// </summary>
/// <remarks>
/// <para><b>Numbers are exact in JSON Schema and doubles in most validators.</b> The bounds are written as the IR's exact decimal
/// text, which is true of the values; a validator that reads numbers as doubles reads a value and a bound each as the nearest
/// double, and rounding never reverses an order, so an inclusive bound is never crossed by rounding. An exclusive one can be, by a
/// value inside it but close to it, so it is written as the inclusive bound one step inside, at the type's scale
/// (<see cref="IrBound.InsideAt"/>), which says the same of the stored values; without a known scale it is written inclusive, which
/// is weaker, and a note says so.</para>
/// <para><b>Steps.</b> <c>decimal(p, 0)</c> is <c>integer</c>, exact everywhere. A decimal's places past the point are not
/// <c>multipleOf</c>: a validator that divides doubles finds 0.07 no multiple of 0.01, and would reject true values. A note says
/// how many places there are.</para>
/// <para><b>Rowsets too.</b> A row is described as <c>FOR JSON</c> writes it (decision 0051), so a <c>bigint</c> is a JSON integer
/// and a decimal a JSON number, in rowsets and JSON results alike.</para>
/// </remarks>
internal static class Numbers
{
    /// <summary>The number's schema, with a note for each fact widened.</summary>
    public static Schema Of(IrScalar scalar)
    {
        var facets = scalar.Facets;
        var notes = new List<SchemaNote>();
        Schema schema;
        switch (scalar.Format)
        {
            case IrFormat.Uint8 or IrFormat.Int16 or IrFormat.Int32 or IrFormat.Int64:
            {
                var (min, max) = Range(scalar.Format.Value);
                var type = new IrFactSource(IrFactSourceKind.Type, scalar.Sql ?? IrWireNames.Of(scalar.Format.Value));
                schema = Exact(facets) ?? Bound(
                    Bound(Schema.OfType("integer"), facets?.Min ?? new IrBound(min, true, type), 0, lower: true, notes),
                    facets?.Max ?? new IrBound(max, true, type), 0, lower: false, notes);
                if (scalar.Format == IrFormat.Int64)
                    notes.Add(new SchemaNote("A bigint, exact only up to 2^53 in a validator that reads numbers as doubles.", JsonSchemaCaveat.Doubles));
                break;
            }

            case IrFormat.Decimal:
            {
                var digits = facets?.Digits;
                schema = Exact(facets) ?? Bound(
                    Bound(Schema.OfType(digits?.Scale == 0 ? "integer" : "number"), facets?.Min, digits?.Scale, lower: true, notes),
                    facets?.Max, digits?.Scale, lower: false, notes);
                if (digits is { Scale: > 0 } && schema.Keywords.ContainsKey(Keyword.Type))
                {
                    var places = digits.Scale == 1 ? "1 decimal place" : $"{digits.Scale} decimal places";
                    notes.Add(new SchemaNote($"At most {places}, which multipleOf would check inexactly.", JsonSchemaCaveat.DecimalSteps));
                }

                if (digits is { Precision: > 15 })
                    notes.Add(new SchemaNote($"{scalar.Sql ?? "The decimal"} has more digits than a double holds, so a validator that reads numbers as doubles checks it on the nearest doubles.", JsonSchemaCaveat.Doubles));
                break;
            }

            case IrFormat.Float when facets?.Values is { Match: IrValueMatch.Exact } floats:
                // FOR JSON writes a float with 16 significant digits, and a double may need 17 to read back as itself, so the value
                // that arrives may not be the literal's double: a set of them is a note, and the bounds still hold.
                schema = Bound(Bound(Schema.OfType("number"), facets.Min, null, lower: true, notes), facets.Max, null, lower: false, notes);
                notes.Add(new SchemaNote($"One of {string.Join(", ", floats.Items.Select(i => i.Text))}, as a float written with 16 significant digits reads back.", JsonSchemaCaveat.Doubles));
                break;

            default:
                schema = Exact(facets) ?? Bound(Bound(Schema.OfType("number"), facets?.Min, null, lower: true, notes), facets?.Max, null, lower: false, notes);
                break;
        }

        return schema.Noted(notes);
    }

    /// <summary><c>enum</c> for an exact set of numbers, written as their exact text; null when there is none.</summary>
    private static Schema? Exact(IrFacets? facets) =>
        facets?.Values is { Match: IrValueMatch.Exact } set && set.Items.All(i => !i.IsString)
            ? set.Items.Count == 0 ? Schema.Never : Schema.Any.With(Keyword.Enum, new SchemaValue.List([.. set.Items.Select(i => new SchemaValue.Number(i.Text))]))
            : null;

    /// <summary>The schema with <paramref name="bound"/> as <c>minimum</c> or <c>maximum</c> (see the remarks); none adds nothing.</summary>
    /// <param name="scale">The values' scale, to make an exclusive bound inclusive; null when unknown.</param>
    private static Schema Bound(Schema schema, IrBound? bound, int? scale, bool lower, List<SchemaNote> notes)
    {
        if (bound is null)
            return schema;
        var keyword = lower ? Keyword.Minimum : Keyword.Maximum;
        if (scale is { } places && bound.InsideAt(places, lower) is { } inside)
            return schema.With(keyword, new SchemaValue.Number(inside));
        if (!bound.Inclusive)
        {
            notes.Add(new SchemaNote(
                $"{(lower ? "Greater" : "Less")} than {bound.Value}, checked as {(lower ? "at least" : "at most")} it, since a value inside an exclusive bound but close to it can read as the bound itself as a double.",
                JsonSchemaCaveat.Doubles));
        }

        return schema.With(keyword, new SchemaValue.Number(bound.Value));
    }

    private static (string Min, string Max) Range(IrFormat format) => format switch
    {
        IrFormat.Uint8 => ("0", "255"),
        IrFormat.Int16 => (short.MinValue.ToString(CultureInfo.InvariantCulture), short.MaxValue.ToString(CultureInfo.InvariantCulture)),
        IrFormat.Int32 => (int.MinValue.ToString(CultureInfo.InvariantCulture), int.MaxValue.ToString(CultureInfo.InvariantCulture)),
        IrFormat.Int64 => (long.MinValue.ToString(CultureInfo.InvariantCulture), long.MaxValue.ToString(CultureInfo.InvariantCulture)),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Only an integer format has a range here."),
    };
}
