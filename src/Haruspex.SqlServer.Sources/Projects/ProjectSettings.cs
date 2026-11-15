using System.Globalization;
using System.Text.RegularExpressions;
using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Sources.Projects;

/// <summary>
/// The database's settings as a project's properties give them: the same <see cref="DatabaseSettings"/> a DACPAC's
/// <c>model.xml</c> records, with the values a build gives a property the project doesn't set.
/// </summary>
/// <remarks>
/// <para>
/// The defaults were observed in packages Microsoft.Build.Sql 2.3.0 built from projects that set none (14 Nov 2026): the
/// collation <c>SQL_Latin1_General_CP1_CI_AS</c>; <c>ANSI_NULL_DEFAULT</c>, <c>ANSI_NULLS</c>, <c>ANSI_PADDING</c>,
/// <c>ANSI_WARNINGS</c>, <c>ARITHABORT</c>, <c>CONCAT_NULL_YIELDS_NULL</c> and <c>QUOTED_IDENTIFIER</c> on; <c>NUMERIC_ROUNDABORT</c>
/// off. A legacy project built by the same build targets gave the same.
/// </para>
/// <para>
/// The compatibility level is <c>CompatibilityMode</c>, else the number of the DSP, the platform the project targets
/// (<c>Microsoft.Data.Tools.Schema.Sql.Sql150DatabaseSchemaProvider</c> is 150). A DSP without one, such as Azure SQL Database's
/// <c>SqlAzureV12DatabaseSchemaProvider</c>, gets the level the build's own DacFx gives it (170 from 2.3.0, observed), which the
/// project doesn't say, so the level is null. A project without a DSP doesn't build (MSB4044, observed under 1.0.0 and 2.3.0), so
/// it is reported, never given a platform.
/// </para>
/// </remarks>
internal static partial class ProjectSettings
{
    /// <summary>The collation a build gives a project that sets none (observed).</summary>
    public const string DefaultCollation = "SQL_Latin1_General_CP1_CI_AS";

    /// <summary>
    /// The settings of <paramref name="project"/>, or null when one isn't read, each such reported in
    /// <paramref name="diagnostics"/>; and the collation, which is read alone when another setting isn't, since it is the one a
    /// read applies.
    /// </summary>
    public static DatabaseSettings? Read(EvaluatedProject project, ICollection<Diagnostic> diagnostics, out string? collation)
    {
        ArgumentNullException.ThrowIfNull(project);
        var read = true;
        collation = Text(project, ProjectXml.DefaultCollation, diagnostics) is { } named ? (named.Length > 0 ? named : DefaultCollation) : null;
        var level = Level(project, diagnostics, ref read);
        bool Flag(string name, bool absent) => Switch(project, name, absent, diagnostics) ?? (read = false);

        var settings = new DatabaseSettings(
            collation ?? DefaultCollation,
            level,
            Flag(ProjectXml.AnsiNulls, absent: true),
            Flag(ProjectXml.AnsiNullDefault, absent: true),
            Flag(ProjectXml.AnsiPadding, absent: true),
            Flag(ProjectXml.AnsiWarnings, absent: true),
            Flag(ProjectXml.ArithAbort, absent: true),
            Flag(ProjectXml.ConcatNullYieldsNull, absent: true),
            Flag(ProjectXml.NumericRoundAbort, absent: false),
            Flag(ProjectXml.QuotedIdentifier, absent: true));
        return read && collation is not null ? settings : null;
    }

    /// <summary>
    /// The value of a property that is <c>True</c> or <c>False</c> (ignoring case), <paramref name="absent"/> when the project
    /// doesn't set it; null, reported, when it is doubted or neither.
    /// </summary>
    public static bool? Switch(EvaluatedProject project, string name, bool absent, ICollection<Diagnostic> diagnostics)
    {
        switch (Text(project, name, diagnostics)?.ToUpperInvariant())
        {
            case null:
                return null;
            case "":
                return absent;
            case "TRUE":
                return true;
            case "FALSE":
                return false;
            default:
                diagnostics.Add(SourceCodes.PartNotRead.At(
                    project.File.At(project.File.Root),
                    $"{Path.GetFileName(project.File.Path)} sets {name} to \"{project.Properties[name]?.Text}\", which a build doesn't read as True or False, so the database's settings are not read from it."));
                return null;
        }
    }

    /// <summary>A property's text, empty when it isn't set; null, reported, when it is doubted.</summary>
    private static string? Text(EvaluatedProject project, string name, ICollection<Diagnostic> diagnostics)
    {
        if (project.Properties[name] is not { } value)
            return "";
        if (value.IsSure)
            return value.Text.Trim();
        Doubt.Report(diagnostics, $"{name}, a setting of the database,", value.Doubts);
        return null;
    }

    /// <summary>The compatibility level: <c>CompatibilityMode</c>, else the DSP's number; null when neither gives one.</summary>
    private static int? Level(EvaluatedProject project, ICollection<Diagnostic> diagnostics, ref bool read)
    {
        var mode = Text(project, ProjectXml.CompatibilityMode, diagnostics);
        var dsp = Text(project, ProjectXml.DSP, diagnostics);
        if (mode is null || dsp is null)
            read = false;
        if (dsp is { Length: 0 })
        {
            diagnostics.Add(SourceCodes.PartNotRead.At(
                project.File.At(project.File.Root),
                $"{Path.GetFileName(project.File.Path)} names no DSP, the platform it targets, which a build requires (MSB4044); its compatibility level is not known unless CompatibilityMode gives it."));
        }

        if (mode is { Length: > 0 })
        {
            if (int.TryParse(mode, NumberStyles.None, CultureInfo.InvariantCulture, out var given))
                return given;
            diagnostics.Add(SourceCodes.PartNotRead.At(
                project.File.At(project.File.Root),
                $"{Path.GetFileName(project.File.Path)} sets CompatibilityMode to \"{mode}\", which isn't a level, so the database's settings are not read from it."));
            read = false;
            return null;
        }

        return dsp is not null && Provider().Match(dsp) is { Success: true } match
            ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)
            : null;
    }

    /// <summary>A DSP that names a version of SQL Server: <c>….Sql160DatabaseSchemaProvider</c>.</summary>
    [GeneratedRegex(@"\.Sql(\d+)DatabaseSchemaProvider$", RegexOptions.CultureInvariant)]
    private static partial Regex Provider();
}
