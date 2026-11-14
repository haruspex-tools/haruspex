namespace Haruspex.SqlServer.Model;

/// <summary>
/// The database's own settings, as a source that records them declares them (a DACPAC does; a folder of scripts doesn't): its
/// default collation, its compatibility level, and the defaults of the <c>SET</c> options a connection that sets nothing gets.
/// </summary>
/// <remarks>
/// <para>
/// Recorded, not applied. The <c>SET</c> defaults are the database's, not the callers': current drivers set these options at
/// login, so a database's default applies only to a connection that sets nothing, and what the callers set is declared in the
/// config file instead (<c>docs/decisions/0038-the-callers-session-is-declared.md</c>, <see cref="SessionOptions"/>). The
/// collation is the one a column without <c>COLLATE</c> takes (<see cref="Column.Collation"/> keeps only what a column
/// declares).
/// </para>
/// <para>
/// A DACPAC writes an option only when it is on, so an option it leaves out is off (observed in <c>tests/dacpacs</c>: a project
/// with <c>AnsiWarnings</c> false has no <c>IsAnsiWarningsOn</c>), and each is known.
/// </para>
/// </remarks>
/// <param name="Collation">The default collation, such as <c>Latin1_General_100_CI_AS</c>.</param>
/// <param name="CompatibilityLevel">The compatibility level (<c>160</c> for SQL Server 2022's), which decides how some syntax and functions behave.</param>
/// <param name="AnsiNulls">Whether <c>ANSI_NULLS</c> is on by default.</param>
/// <param name="AnsiNullDefault">Whether a column declared without <c>NULL</c> or <c>NOT NULL</c> is nullable by default (<c>ANSI_NULL_DEFAULT</c>).</param>
/// <param name="AnsiPadding">Whether <c>ANSI_PADDING</c> is on by default.</param>
/// <param name="AnsiWarnings">Whether <c>ANSI_WARNINGS</c> is on by default.</param>
/// <param name="ArithAbort">Whether <c>ARITHABORT</c> is on by default.</param>
/// <param name="ConcatNullYieldsNull">Whether <c>CONCAT_NULL_YIELDS_NULL</c> is on by default.</param>
/// <param name="NumericRoundAbort">Whether <c>NUMERIC_ROUNDABORT</c> is on by default.</param>
/// <param name="QuotedIdentifier">Whether <c>QUOTED_IDENTIFIER</c> is on by default.</param>
public sealed record DatabaseSettings(
    string Collation,
    int CompatibilityLevel,
    bool AnsiNulls,
    bool AnsiNullDefault,
    bool AnsiPadding,
    bool AnsiWarnings,
    bool ArithAbort,
    bool ConcatNullYieldsNull,
    bool NumericRoundAbort,
    bool QuotedIdentifier);
