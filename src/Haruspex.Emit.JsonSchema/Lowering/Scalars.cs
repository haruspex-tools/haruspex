using Haruspex.Emit.JsonSchema.Model;
using Haruspex.Ir;

namespace Haruspex.Emit.JsonSchema.Lowering;

/// <summary>
/// A scalar as JSON writes it, with its origins first in its description. Every value is described in its JSON form, as SQL
/// Server's <c>FOR JSON</c> writes it, in a rowset as much as in a JSON result (decision 0051): what a value looks like is the
/// front end's to say (the IR's README, "Logical kinds"), and the SQL Server front end's README lists it ("Scalars in JSON").
/// </summary>
/// <remarks>
/// Dates, times and GUIDs are strings in fixed formats. Each has a <c>pattern</c>, which every validator checks, and where an
/// RFC 3339 <c>format</c> fits exactly, that too: <c>date</c>, <c>date-time</c> (which requires an offset, so only for a
/// <c>datetime</c> that has one) and <c>uuid</c>. In draft 2020-12 a format is an annotation unless a validator is asked to assert
/// it, so the pattern is the check everywhere, and the format adds what a pattern can't (a real calendar date) where it is asserted,
/// and names the kind for code generators. A time, and a date and time without an offset, have no format that fits, since RFC
/// 3339's <c>time</c> and <c>date-time</c> both require one. A fraction of a second has as many digits as the type's scale and
/// is left out when zero, so it is optional in every pattern.
/// </remarks>
internal static class Scalars
{
    private const string DatePattern = @"^\d{4}-\d{2}-\d{2}$";
    private const string TimePattern = @"^\d{2}:\d{2}:\d{2}(?:\.\d+)?$";
    private const string LocalPattern = @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?$";
    private const string OffsetPattern = @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$";
    private const string Base64Pattern = "^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$";
    private const string UuidPattern = "^[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}$";

    /// <summary>The scalar's schema: its kind's, with its SQL type and facts' origins said before any note of its own.</summary>
    public static Schema Of(IrScalar scalar)
    {
        var schema = scalar.Kind switch
        {
            IrScalarKind.String => Strings.Of(scalar),
            IrScalarKind.Number => Numbers.Of(scalar),
            IrScalarKind.Boolean => Schema.OfType("boolean"),
            IrScalarKind.Date => Patterned(DatePattern, "date"),
            IrScalarKind.Time => Patterned(TimePattern),
            IrScalarKind.Datetime => scalar.Format switch
            {
                IrFormat.Local => Patterned(LocalPattern),
                IrFormat.Offset => Patterned(OffsetPattern, "date-time"),

                // Without a format whether there is an offset is not known, so no form is claimed.
                null => Schema.OfType("string"),
                _ => throw new ArgumentOutOfRangeException(nameof(scalar), scalar.Format, "A date and time's format is local or offset."),
            },
            IrScalarKind.Uuid => Patterned(UuidPattern, "uuid"),
            IrScalarKind.Bytes => Base64(scalar.Facets?.Length),
        };
        return schema with { Notes = [.. Origins.Of(scalar), .. schema.Notes] };
    }

    /// <summary>
    /// A binary value: the base64 text <c>FOR JSON</c> writes, padded and on one line (observed: <c>FOR JSON</c> writes binary as
    /// base64 in <c>BinaryObservationTests</c>), as long as its bytes take, four characters for every three bytes or part of three:
    /// exactly that for a fixed length, at most that otherwise. A rowset's value is described as <c>FOR JSON</c> would write it,
    /// as every value of a rowset is. Why: docs/decisions/0082-a-binary-value-is-a-buffer-in-a-rowset-and-base64-in-json.md.
    /// </summary>
    private static Schema Base64(IrLength? length)
    {
        var schema = Patterned(Base64Pattern).With(Keyword.ContentEncoding, new SchemaValue.Text("base64"));
        if (length is null)
            return schema;
        var characters = (int)Math.Min(int.MaxValue, 4L * ((length.Value + 2L) / 3));
        return schema.With(Keyword.MinLength, length.IsFixed && characters > 0 ? characters : null).With(Keyword.MaxLength, characters);
    }

    private static Schema Patterned(string pattern, string? format = null)
    {
        var schema = Schema.OfType("string").With(Keyword.Pattern, new SchemaValue.Text(pattern));
        return format is null ? schema : schema.With(Keyword.Format, new SchemaValue.Text(format));
    }
}
