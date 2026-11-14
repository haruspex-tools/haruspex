namespace Haruspex.SqlServer.Model;

/// <summary>
/// The <c>SET</c> options the callers' connections are declared to set, each null when it is not known. They are the
/// callers', not the database's: a database's defaults apply only to a connection that sets nothing, and current drivers set
/// these at login (<c>docs/decisions/0038-the-callers-session-is-declared.md</c>). Options fixed for a module when it is created
/// (<c>ANSI_NULLS</c>, <c>QUOTED_IDENTIFIER</c>) are not here: they are read from its source.
/// </summary>
/// <remarks>
/// Only the options a rule of inference reads are here, so that a declaration never looks as if it changed something it
/// does not. <c>NUMERIC_ROUNDABORT</c>, <c>DATEFIRST</c>, <c>LANGUAGE</c> and <c>DATEFORMAT</c> change no claim haruspex
/// makes; the decision says why for each.
/// </remarks>
/// <param name="AnsiWarnings">Whether <c>ANSI_WARNINGS</c> is on.</param>
/// <param name="ArithAbort">Whether <c>ARITHABORT</c> is on.</param>
/// <param name="ConcatNullYieldsNull">Whether <c>CONCAT_NULL_YIELDS_NULL</c> is on.</param>
public sealed record SessionOptions(bool? AnsiWarnings = null, bool? ArithAbort = null, bool? ConcatNullYieldsNull = null)
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
    ];

    /// <summary>The names of the options, in order.</summary>
    public static IReadOnlyList<string> Names { get; } = [AnsiWarningsName, ArithAbortName, ConcatNullYieldsNullName];

    /// <summary>These options with the one named <paramref name="name"/> (one of <see cref="Names"/>) declared.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The name is not one of <see cref="Names"/>.</exception>
    public SessionOptions With(string name, bool on) => name switch
    {
        AnsiWarningsName => this with { AnsiWarnings = on },
        ArithAbortName => this with { ArithAbort = on },
        ConcatNullYieldsNullName => this with { ConcatNullYieldsNull = on },
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Not the name of a session option."),
    };

    private const string AnsiWarningsName = "ansiWarnings";
    private const string ArithAbortName = "arithAbort";
    private const string ConcatNullYieldsNullName = "concatNullYieldsNull";

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
}
