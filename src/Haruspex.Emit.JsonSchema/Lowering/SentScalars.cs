using System.Globalization;
using System.Text.RegularExpressions;
using Haruspex.Emit.JsonSchema.Model;
using Haruspex.Ir;

namespace Haruspex.Emit.JsonSchema.Lowering;

/// <summary>
/// A scalar a client sends, in a table type's row, rather than one the database returns: a date or time is an ISO 8601 string of
/// exactly the form its type holds, since the server rounds a finer fraction without an error, and a <c>bigint</c> is a number
/// or the string of its digits, as the parameters' Zod takes it; a string's fixed length is a maximum, since a shorter one is padded
/// (<c>docs/decisions/0054-a-parameter-is-checked-against-its-declared-type.md</c>,
/// <c>docs/decisions/0055-dates-and-times-are-sent-as-iso-8601-strings.md</c>). Every other scalar is as a result's
/// (<see cref="Scalars"/>).
/// </summary>
/// <remarks>
/// The forms are read from the type the IR keeps (<see cref="IrScalar.Sql"/>), spelt as decision 0041 fixes, as the Zod emitter
/// reads them: the IR has no fact for a time's fraction, and its kinds don't tell <c>datetime</c> from <c>datetime2</c>. A date's
/// range is no pattern's to check, so a <c>datetime</c> before 1753 or a <c>smalldatetime</c> outside its range is left to the
/// server, which refuses it with an error rather than changing it. A type not in the list takes the result's form, with a note.
/// </remarks>
internal static partial class SentScalars
{
    private const string Day = @"\d{4}-\d{2}-\d{2}";
    private const string Clock = @"\d{2}:\d{2}:\d{2}";

    /// <summary>The schema of a value of <paramref name="scalar"/> a client sends.</summary>
    public static Schema Of(IrScalar scalar) => scalar switch
    {
        { Kind: IrScalarKind.Date or IrScalarKind.Time or IrScalarKind.Datetime } => DateTime(scalar),
        { Kind: IrScalarKind.Number, Format: IrFormat.Int64 } => BigInt(scalar),

        // A fixed length is a maximum too, since a shorter value sent is padded, and a minimum a CHECK proves of what is stored is
        // the server's to refuse: as the parameters' Zod checks a string.
        { Kind: IrScalarKind.String, Facets: { } facets } => Scalars.Of(scalar with
        {
            Facets = facets with { Length = facets.Length is { } length ? length with { IsFixed = false } : null, MinLength = null },
        }),
        // A binary value sent shorter than a fixed length is padded, as a string is.
        { Kind: IrScalarKind.Bytes, Facets.Length: { IsFixed: true } length } => Scalars.Of(scalar with { Facets = scalar.Facets with { Length = length with { IsFixed = false } } }),
        _ => Scalars.Of(scalar),
    };

    /// <summary>
    /// The pattern of a time or a date and time as <c>FOR JSON</c> writes a value of <paramref name="scalar"/>'s type, anchored:
    /// seconds always, a fraction of exactly the type's scale or none, three digits ending in 0, 3 or 7 for <c>datetime</c>, none for
    /// <c>smalldatetime</c>, and an offset only for <c>datetimeoffset</c> (observed: <c>ForJsonObservationTests</c>). Null for a
    /// date, whose one form the result's pattern already is, and for a type not in the list.
    /// </summary>
    public static string? Written(IrScalar scalar)
    {
        var (name, scale) = Type(scalar);
        var pattern = (name, scale) switch
        {
            ("time", { } n) => Clock + Exactly(n),
            ("datetime2", { } n) => Day + "T" + Clock + Exactly(n),
            ("datetimeoffset", { } n) => Day + "T" + Clock + Exactly(n) + "(?:Z|[+-]\\d{2}:\\d{2})",
            ("datetime", null) => Day + "T" + Clock + @"(?:\.\d{2}[037])?",
            ("smalldatetime", null) => Day + @"T\d{2}:\d{2}:00",
            _ => null,
        };
        return pattern is null ? null : "^" + pattern + "$";
    }

    /// <summary>A date and time type's name and scale, as decision 0041 spells it; a null name when it isn't one.</summary>
    private static (string? Name, int? Scale) Type(IrScalar scalar)
    {
        var type = scalar.Sql is { } sql ? Spelling().Match(sql) : Match.Empty;
        var name = type.Success ? type.Groups["name"].Value : null;
        var scale = type.Groups["scale"].Success ? int.Parse(type.Groups["scale"].Value, CultureInfo.InvariantCulture) : (int?)null;
        return (name, scale);
    }

    /// <summary>Exactly <paramref name="scale"/> digits of a second, or none, as <c>FOR JSON</c> writes them.</summary>
    private static string Exactly(int scale) => scale == 0 ? "" : $@"(?:\.\d{{{scale.ToString(CultureInfo.InvariantCulture)}}})?";

    private static Schema DateTime(IrScalar scalar)
    {
        var (name, scale) = Type(scalar);
        var pattern = (name, scale) switch
        {
            ("date", null) => Day,
            ("time", { } n) => Clock + Fraction(n),
            ("datetime2", { } n) => Day + "T" + Clock + Fraction(n),
            ("datetimeoffset", { } n) => Day + "T" + Clock + Fraction(n) + "(?:Z|[+-]\\d{2}:\\d{2})",

            // Three-hundredths of a second, written .xx0, .xx3 or .xx7.
            ("datetime", null) => Day + "T" + Clock + @"(?:\.\d(?:\d[037]?)?)?",
            ("smalldatetime", null) => Day + @"T\d{2}:\d{2}:00",
            _ => null,
        };
        if (pattern is null)
            return Scalars.Of(scalar).Noted(new SchemaNote($"{scalar.Sql ?? "Its type"} isn't a type this document knows the precision of, so a finer fraction may be rounded without an error."));
        var schema = Schema.OfType("string").With(Keyword.Pattern, new SchemaValue.Text("^" + pattern + "$"));
        if (name == "date")
            schema = schema.With(Keyword.Format, new SchemaValue.Text("date"));
        var note = name == "date"
            ? "Sent as text of exactly this form, which the server converts exactly."
            : "Sent as text of exactly this form, which the server converts exactly; a finer fraction would be rounded without an error.";
        return schema with { Notes = [.. Origins.Of(scalar), new SchemaNote(note)] };
    }

    /// <summary>
    /// A <c>bigint</c>: a JSON integer in its range, or the string of its digits, which a number past 2^53 must be to arrive exactly.
    /// The pattern doesn't bound the digits' value: the driver refuses one outside the range before the server sees it (observed:
    /// A_bigint_outside_its_range_in_a_row_is_refused_by_the_driver_before_the_server_sees_it).
    /// </summary>
    private static Schema BigInt(IrScalar scalar)
    {
        var number = Scalars.Of(scalar);
        var digits = Schema.OfType("string").With(Keyword.Pattern, new SchemaValue.Text(@"^-?\d{1,19}$"));
        var either = Schema.Any.With(Keyword.AnyOf, new SchemaValue.List([new SchemaValue.Nested(number with { Notes = [] }), new SchemaValue.Nested(digits)]));
        return either with { Notes = [.. number.Notes, new SchemaNote("Or the string of its digits, as a rowset hands a bigint over, which a value past 2^53 must be to arrive exactly; the mssql driver refuses one outside the range before sending it.")] };
    }

    /// <summary>Up to <paramref name="scale"/> digits of a second, or none.</summary>
    private static string Fraction(int scale) => scale == 0 ? "" : $@"(?:\.\d{{1,{scale.ToString(CultureInfo.InvariantCulture)}}})?";

    /// <summary>A date and time type as decision 0041 spells it: its name, and its scale where it has one.</summary>
    [GeneratedRegex(@"^(?<name>date|time|datetime2|datetimeoffset|datetime|smalldatetime)(\((?<scale>[0-7])\))?$", RegexOptions.CultureInvariant)]
    private static partial Regex Spelling();
}
