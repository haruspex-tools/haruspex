using System.Globalization;
using System.Text.RegularExpressions;

namespace Haruspex.Ir;

/// <summary>
/// The range and fraction of a date and time type, read from the SQL type a scalar keeps (<see cref="IrScalar.Sql"/>), spelt as
/// decision 0041 fixes, as the Zod emitter's <c>DateTimes</c> reads them. The IR has no fact for either, so a parameter's
/// date or time is compared by its type: <see cref="IrFit"/> asks whether a procedure takes what an application sends, and
/// <see cref="IrCombination"/> which of two types both procedures take.
/// </summary>
internal static partial class IrDateTimes
{
    /// <summary>
    /// Whether every date or time of the type <paramref name="given"/> is taken exactly by the type <paramref name="accepting"/>:
    /// its range within the other's and no finer fraction. A type outside the list decision 0041 spells can't be shown to fit.
    /// Observed: a value out of range fails the call (8114), and a finer fraction is rounded without an error
    /// (<c>docs/decisions/0055-dates-and-times-are-sent-as-iso-8601-strings.md</c>).
    /// </summary>
    public static bool Takes(string? accepting, string? given) =>
        Type(accepting) is { } wide && Type(given) is { } narrow
        && string.CompareOrdinal(wide.Earliest, narrow.Earliest) <= 0 && string.CompareOrdinal(narrow.Latest, wide.Latest) <= 0
        && narrow.Fraction switch
        {
            // Whole minutes or whole days: any type of the kind holds them.
            Fraction.None => true,

            // datetime's thousandths end in 0, 3 or 7 as it writes them, which a type of three places or more holds exactly.
            Fraction.Datetime => wide.Fraction is Fraction.Datetime || (wide.Fraction is Fraction.Digits && wide.Digits >= 3),

            // datetime holds every fraction of two places (a hundredth is three of its three-hundredths), but not three.
            _ => wide.Fraction switch
            {
                Fraction.Digits => narrow.Digits <= wide.Digits,
                Fraction.Datetime => narrow.Digits <= 2,
                _ => false,
            },
        };

    /// <summary>A date and time type's range, as ISO text, which orders as time does; and its fraction of a second.</summary>
    private static (string Earliest, string Latest, Fraction Fraction, int Digits)? Type(string? sql) => Spelling().Match(sql ?? "") switch
    {
        { Success: false } => null,
        var type => (type.Groups["name"].Value, type.Groups["scale"].Success ? int.Parse(type.Groups["scale"].Value, CultureInfo.InvariantCulture) : (int?)null) switch
        {
            ("date", null) => ("0001-01-01", "9999-12-31", Fraction.None, 0),
            ("time", { } n) => ("00:00:00", "23:59:59", Fraction.Digits, n),
            ("datetime2" or "datetimeoffset", { } n) => ("0001-01-01", "9999-12-31T23:59:59", Fraction.Digits, n),
            ("datetime", null) => ("1753-01-01", "9999-12-31T23:59:59", Fraction.Datetime, 3),
            ("smalldatetime", null) => ("1900-01-01", "2079-06-06T23:59:00", Fraction.None, 0),
            _ => null,
        },
    };

    /// <summary>A date and time type as decision 0041 spells it: its name, and its scale where it has one.</summary>
    [GeneratedRegex(@"^(?<name>date|time|datetime2|datetimeoffset|datetime|smalldatetime)(\((?<scale>[0-7])\))?$", RegexOptions.CultureInvariant)]
    private static partial Regex Spelling();

    /// <summary>How finely a date and time type holds a second.</summary>
    private enum Fraction
    {
        /// <summary>Not at all: whole days, or whole minutes.</summary>
        None,

        /// <summary>To so many decimal places.</summary>
        Digits,

        /// <summary>To three-hundredths, written to three places ending in 0, 3 or 7: <c>datetime</c>.</summary>
        Datetime,
    }
}
