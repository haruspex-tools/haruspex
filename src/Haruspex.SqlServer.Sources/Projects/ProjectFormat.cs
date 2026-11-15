namespace Haruspex.SqlServer.Sources.Projects;

/// <summary>Which kind of database project a file is, which decides which scripts its build reads.</summary>
/// <param name="SdkVersion">
/// For a <c>Microsoft.Build.Sql</c> project, the SDK's version as the project names it (<c>2.3.0</c>), or empty when it names
/// none (a version in <c>global.json</c>); null for a legacy SSDT project, which lists every script it builds.
/// </param>
internal sealed record ProjectFormat(string? SdkVersion)
{
    /// <summary>A legacy SSDT project.</summary>
    public static ProjectFormat Legacy { get; } = new(SdkVersion: null);

    /// <summary>Whether it is a <c>Microsoft.Build.Sql</c> project, whose build reads every <c>.sql</c> file under its folder by default.</summary>
    public bool IsSdkStyle => SdkVersion is not null;
}
