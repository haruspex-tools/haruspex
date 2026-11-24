namespace Haruspex.SqlServer.Model;

/// <summary>
/// The <c>SET</c> options the callers' connections are declared to set, and whether the callers hold <c>UNMASK</c>, each null when
/// it is not known. They are the
/// callers', not the database's: a database's defaults apply only to a connection that sets nothing, and current drivers set
/// these at login (<c>docs/decisions/0038-the-callers-session-is-declared.md</c>). Options fixed for a module when it is created
/// (<c>ANSI_NULLS</c>, <c>QUOTED_IDENTIFIER</c>) are not here: they are read from its source.
/// </summary>
/// <remarks>
/// Only the options a rule of inference reads are here, so that a declaration never looks as if it changed something it
/// does not. <c>DATEFIRST</c>, <c>LANGUAGE</c> and <c>DATEFORMAT</c> change no claim haruspex makes; the decision says why
/// for each.
/// </remarks>
/// <param name="AnsiWarnings">Whether <c>ANSI_WARNINGS</c> is on.</param>
/// <param name="ArithAbort">Whether <c>ARITHABORT</c> is on.</param>
/// <param name="ConcatNullYieldsNull">Whether <c>CONCAT_NULL_YIELDS_NULL</c> is on.</param>
/// <param name="NumericRoundAbort">Whether <c>NUMERIC_ROUNDABORT</c> is on.</param>
/// <param name="Unmask">
/// Whether the callers hold the <c>UNMASK</c> permission, so that a masked column gives them its values rather than its mask
/// (docs/decisions/0118-a-masked-column-claims-only-its-type-nullability-and-greatest-length.md). Not an option a session sets but
/// a permission of the user it runs as, declared beside them because, like them, it is the callers' and out of sight.
/// </param>
public sealed record SessionOptions(bool? AnsiWarnings = null, bool? ArithAbort = null, bool? ConcatNullYieldsNull = null, bool? NumericRoundAbort = null, bool? Unmask = null)
{
    /// <summary>Nothing known about the callers' sessions: every rule assumes the option that claims least.</summary>
    public static SessionOptions NotKnown { get; } = new();

    /// <summary>Whether no option is declared.</summary>
    public bool IsNotKnown => this == NotKnown;

    /// <summary>
    /// Each option by the name a declaration gives it (the config file's <c>session</c> block, and the IR's), in that name's
    /// order, with its value or null.
    /// </summary>
    public IReadOnlyList<(string Name, bool? On)> ByName =>
    [
        (AnsiWarningsName, AnsiWarnings),
        (ArithAbortName, ArithAbort),
        (ConcatNullYieldsNullName, ConcatNullYieldsNull),
        (NumericRoundAbortName, NumericRoundAbort),
        (UnmaskName, Unmask),
    ];

    /// <summary>The names of the options, in order.</summary>
    public static IReadOnlyList<string> Names { get; } = [AnsiWarningsName, ArithAbortName, ConcatNullYieldsNullName, NumericRoundAbortName, UnmaskName];

    /// <summary>These options with the one named <paramref name="name"/> (one of <see cref="Names"/>) declared.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The name is not one of <see cref="Names"/>.</exception>
    public SessionOptions With(string name, bool on) => name switch
    {
        AnsiWarningsName => this with { AnsiWarnings = on },
        ArithAbortName => this with { ArithAbort = on },
        ConcatNullYieldsNullName => this with { ConcatNullYieldsNull = on },
        NumericRoundAbortName => this with { NumericRoundAbort = on },
        UnmaskName => this with { Unmask = on },
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Not the name of a session option."),
    };

    private const string AnsiWarningsName = "ansiWarnings";
    private const string ArithAbortName = "arithAbort";
    private const string ConcatNullYieldsNullName = "concatNullYieldsNull";
    private const string NumericRoundAbortName = "numericRoundAbort";
    private const string UnmaskName = "unmask";

    /// <summary>
    /// Whether an overflow, a division by zero or an invalid floating point operation is an error, which fails the call,
    /// rather than NULL. It is NULL only when <c>ANSI_WARNINGS</c> and <c>ARITHABORT</c> are both off: either on makes it an
    /// error, <c>ANSI_WARNINGS</c> on even with <c>ARITHABORT</c> set off after it (observed:
    /// <c>An_overflow_is_null_only_when_ansi_warnings_and_arithabort_are_both_off</c>).
    /// </summary>
    public bool OverflowIsAnError => AnsiWarnings == true || ArithAbort == true;

    /// <summary>
    /// Whether string <c>+</c> with one operand NULL gives the other, as it does with <c>CONCAT_NULL_YIELDS_NULL</c> off
    /// (observed: <c>With_concat_null_yields_null_off_a_string_plus_null_is_the_string</c>). Not known claims the NULL.
    /// </summary>
    public bool ConcatenatingNullKeepsTheString => ConcatNullYieldsNull == false;

    /// <summary>
    /// Whether a decimal rounded to fewer places may be NULL. With <c>NUMERIC_ROUNDABORT</c> off it is rounded silently; on, it
    /// is an error, which fails the call, unless <c>ANSI_WARNINGS</c> and <c>ARITHABORT</c> are both off, when it is NULL, as an
    /// overflow is (<see cref="OverflowIsAnError"/>). Under <c>TRY_CAST</c> and <c>TRY_CONVERT</c>, an
    /// <paramref name="attempt"/>, it is NULL whenever <c>NUMERIC_ROUNDABORT</c> is on, since TRY_ turns the error into NULL
    /// (observed: <c>A_loss_of_precision_is_null_when_numeric_roundabort_is_on_and_ansi_warnings_and_arithabort_are_off</c>,
    /// <c>Under_try_cast_a_loss_of_precision_is_null_whenever_numeric_roundabort_is_on</c>).
    /// </summary>
    public bool RoundingMayBeNull(bool attempt) => NumericRoundAbort != false && (attempt || !OverflowIsAnError);

    /// <summary>
    /// Whether a masked column's values reach the callers, rather than its mask: only when they are declared to hold
    /// <c>UNMASK</c>. Not known claims the mask, which a caller without the permission reads (observed in
    /// <c>MaskingObservationTests</c>).
    /// </summary>
    public bool MasksAreLifted => Unmask == true;
}
