using System.Globalization;
using System.Text.RegularExpressions;
using Haruspex.Emit.Zod.Model;
using Haruspex.Ir;
using static Haruspex.Emit.Zod.Model.ZodExpression;

namespace Haruspex.Emit.Zod.Lowering;

/// <summary>
/// A date and time parameter: an ISO 8601 string of exactly the form its type holds, with no finer fraction and no offset it
/// would drop, in its range; and a date and time in JSON, as the server writes it (<see cref="Written"/>). The string is sent as text and SQL Server converts it, exactly, in every session's language
/// (<c>docs/decisions/0055-dates-and-times-are-sent-as-iso-8601-strings.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// The IR has no fact for a date's range or a time's fraction, and its kinds don't tell <c>datetime</c> from <c>datetime2</c>, so
/// these rules are read from the type the IR keeps (<see cref="IrScalar.Sql"/>), spelt as decision 0041 fixes. That is the only
/// SQL this emitter reads: a type's name from a closed list, never a statement. A type not in the list gets the kind's ISO format
/// and a note that a finer fraction may be rounded.
/// </para>
/// <para>
/// What the server does with each was observed (<c>ParameterObservationTests</c>): a finer fraction is rounded without an error,
/// <c>datetime</c> holds three-hundredths of a second (written .xx0, .xx3 or .xx7) and <c>smalldatetime</c> whole minutes, a
/// <c>datetime2</c> drops an offset and a <c>datetimeoffset</c> takes none as zero, and <c>datetime</c> reads <c>yyyy-mm-dd</c>
/// without a time in the session's date order, so the form with <c>T</c> and seconds is the one accepted. The ranges are compared
/// as text, which for strings of one fixed form is their order in time.
/// </para>
/// </remarks>
internal static partial class DateTimes
{
    private const string Day = @"\d{4}-\d{2}-\d{2}";
    private const string Clock = @"\d{2}:\d{2}:\d{2}";

    /// <summary>The schema of a value of <paramref name="scalar"/>, a date, a time or both.</summary>
    public static ZodExpression Of(IrScalar scalar)
    {
        var (name, scale) = Type(scalar);
        // Each form has its own error, which says what was expected: a caller who sent toISOString()'s output reads that, rather
        // than the pattern.
        return (name, scale) switch
        {
            ("date", null) => Iso("date"),
            ("time", { } n) => Iso("time").Then("regex", new Pattern(Clock + Fraction(n)), Error($"Expected a time({Count(n)}) such as 08:30:00, {Seconds(n)}")),
            ("datetime2", { } n) => Local().Then("regex", new Pattern(Day + "T" + Clock + Fraction(n)), Error($"Expected a datetime2({Count(n)}) such as 2026-11-16T08:30:00, {Seconds(n)} and no offset")),
            ("datetimeoffset", { } n) => Iso("datetime", Option("offset")).Then(
                "regex",
                new Pattern(Day + "T" + Clock + Fraction(n) + @"(Z|[+-]\d{2}:\d{2})"),
                Error($"Expected a datetimeoffset({Count(n)}) such as 2026-11-16T08:30:00+05:30, {Seconds(n)} and an offset")),

            // Three-hundredths of a second, which are .xx0, .xx3 and .xx7 written to three places; and 9999-12-31T23:59:59.997, the
            // greatest datetime, is the greatest value of this form, so only the least needs a check.
            ("datetime", null) => Local().Then("regex", new Pattern(Day + "T" + Clock + @"(\.\d(\d[037]?)?)?"), Error("Expected a datetime such as 2026-11-16T08:30:00.997, with no offset, in thousandths of a second that end in 0, 3 or 7"))
                .Then("refine", new TextRange("1753-01-01", null), Error("Before 1753-01-01, the earliest datetime")),
            ("smalldatetime", null) => Local().Then("regex", new Pattern(Day + @"T\d{2}:\d{2}:00"), Error("Expected a smalldatetime such as 2026-11-16T08:30:00, in whole minutes, with no offset"))
                .Then("refine", new TextRange("1900-01-01", "2079-06-06T23:59:00"), Error("Outside 1900-01-01 to 2079-06-06T23:59:00, the range of smalldatetime")),
            _ => Unknown(scalar),
        };
    }

    /// <summary>
    /// A date or time as <c>FOR JSON</c> and the JSON constructors write a value of <paramref name="scalar"/>'s type: seconds always,
    /// a fraction of exactly the type's scale or none (none when it is zero), three digits ending in 0, 3 or 7 for <c>datetime</c>,
    /// none for <c>smalldatetime</c>, and an offset only for <c>datetimeoffset</c>, a zero one as <c>Z</c> (observed:
    /// <c>ForJsonObservationTests.For_json_writes_each_type_exactly_so</c>; the IR's README, "Scalars in JSON"). Null for a type not
    /// in the list, such as a declared result's, which has no SQL type and was written by whatever wrote its JSON.
    /// </summary>
    public static ZodExpression? Written(IrScalar scalar)
    {
        var (name, scale) = Type(scalar);
        return (name, scale) switch
        {
            ("date", null) => Iso("date"),
            ("time", { } n) => Iso("time").Then("regex", new Pattern(Clock + WrittenFraction(n))),
            ("datetime2", { } n) => Local().Then("regex", new Pattern(Day + "T" + Clock + WrittenFraction(n))),
            ("datetimeoffset", { } n) => Iso("datetime", Option("offset")).Then("regex", new Pattern(Day + "T" + Clock + WrittenFraction(n) + @"(Z|[+-]\d{2}:\d{2})")),
            ("datetime", null) => Local().Then("regex", new Pattern(Day + "T" + Clock + @"(\.\d{2}[037])?")),
            ("smalldatetime", null) => Local().Then("regex", new Pattern(Day + @"T\d{2}:\d{2}:00")),
            _ => null,
        };
    }

    /// <summary>A date and time type's name and scale, as decision 0041 spells it; a null name when it isn't one.</summary>
    private static (string? Name, int? Scale) Type(IrScalar scalar)
    {
        var type = scalar.Sql is { } sql ? Spelling().Match(sql) : Match.Empty;
        var name = type.Success ? type.Groups["name"].Value : null;
        var scale = type.Groups["scale"].Success ? int.Parse(type.Groups["scale"].Value, CultureInfo.InvariantCulture) : (int?)null;
        return (name, scale);
    }

    /// <summary>A type not in the list: the kind's ISO format, whose fraction the type may round.</summary>
    private static ZodExpression Unknown(IrScalar scalar)
    {
        var format = scalar.Kind switch
        {
            IrScalarKind.Date => Iso("date"),
            IrScalarKind.Time => Iso("time"),
            _ => scalar.Format == IrFormat.Offset ? Iso("datetime", Option("offset")) : Local(),
        };
        return scalar.Kind == IrScalarKind.Date
            ? format
            : new Noted(format, new ZodNote($"{scalar.Sql ?? "Its type"} isn't a type this module knows the precision of, so a finer fraction may be rounded without an error."));
    }

    /// <summary>What a type of <paramref name="scale"/> takes of a second, said: whole seconds, or at most so many digits.</summary>
    private static string Seconds(int scale) => scale == 0 ? "in whole seconds" : $"with at most {Count(scale)} digits of a second";

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Exactly <paramref name="scale"/> digits of a second, or none, as <c>FOR JSON</c> writes them.</summary>
    private static string WrittenFraction(int scale) => scale == 0 ? "" : $@"(\.\d{{{scale.ToString(CultureInfo.InvariantCulture)}}})?";

    /// <summary>Up to <paramref name="scale"/> digits of a second, or none.</summary>
    private static string Fraction(int scale) => scale == 0 ? "" : $@"(\.\d{{1,{scale.ToString(CultureInfo.InvariantCulture)}}})?";

    private static Call Local() => Iso("datetime", Option("local"));

    private static ObjectLiteral Option(string name) => new([new ZodProperty(name, new ZodExpression.Boolean(true))]);

    private static ObjectLiteral Error(string message) => new([new ZodProperty("error", new Text(message))]);

    private static Call Iso(string format, params ZodExpression[] arguments) => new(new Name(ZodModule.ZodName + ".iso"), format, [.. arguments]);

    /// <summary>A date and time type as decision 0041 spells it: its name, and its scale where it has one.</summary>
    [GeneratedRegex(@"^(?<name>date|time|datetime2|datetimeoffset|datetime|smalldatetime)(\((?<scale>[0-7])\))?$", RegexOptions.CultureInvariant)]
    private static partial Regex Spelling();
}
