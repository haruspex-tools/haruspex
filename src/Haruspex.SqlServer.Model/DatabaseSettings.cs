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
/// Every option is known: a DACPAC writes each one only when it differs from DacFx's own default, which is on for
/// <c>ANSI_PADDING</c> and <c>QUOTED_IDENTIFIER</c> and off for the rest (observed: projects with every option on and every option
/// off, 14 Nov 2026). The compatibility level is not always written: a package extracted at its provider's level has none,
/// and then it is the level of the provider the package names, or null for one that names no version.
/// </para>
/// </remarks>
/// <param name="Collation">The default collation, such as <c>Latin1_General_100_CI_AS</c>.</param>
/// <param name="CompatibilityLevel">
/// The compatibility level (<c>160</c> for SQL Server 2022's), which decides how some syntax and functions behave; null when the
/// source doesn't say.
/// </param>
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
    int? CompatibilityLevel,
    bool AnsiNulls,
    bool AnsiNullDefault,
    bool AnsiPadding,
    bool AnsiWarnings,
    bool ArithAbort,
    bool ConcatNullYieldsNull,
    bool NumericRoundAbort,
    bool QuotedIdentifier);
