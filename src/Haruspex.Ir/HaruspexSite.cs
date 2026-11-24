namespace Haruspex.Ir;

/// <summary>
/// The site that serves every address haruspex writes into a file it doesn't own: the schemas a config file, a report and an IR
/// file are checked against, and the pages a diagnostic, a reason and the command's help link to. Each address is built from
/// <see cref="Host"/>, a domain the maintainer owns, so that none depends on where the repository is kept and the repository can
/// move without one changing (docs/decisions/0121-every-address-haruspex-writes-is-served-from-haruspex-tools.md). The site is built from
/// the repository by <c>site/build.mjs</c>, whose layout is the one below; <c>SiteTests</c> holds the two together.
/// </summary>
public static class HaruspexSite
{
    /// <summary>The site's origin, which SARIF names as the tool's own page; the site's root leads to the repository.</summary>
    public const string Host = "https://haruspex.tools";

    /// <summary>
    /// The config file's and the report's schemas, under the name of what they were taken from: <c>main/</c>, or a release's tag
    /// (<c>v1.2.3/</c>), whose schemas are that tag's.
    /// </summary>
    public const string Schemas = Host + "/schemas/";

    /// <summary>The IR's schemas, one per version, named for it (<c>0.30.schema.json</c>): every version any release or <c>main</c> has had.</summary>
    public const string IrSchemas = Schemas + "ir/";

    /// <summary>A diagnostic code's page, by its code (<c>HAR2009</c>); the folder itself is the index of every code.</summary>
    public const string Diagnostics = Host + "/diagnostics/";

    /// <summary>An IR code's page (a null rule, an unknown reason or a cardinality), by its name in the IR (<c>count-overflow</c>).</summary>
    public const string Reasons = Host + "/reasons/";

    /// <summary>A page of the documentation, by its name in <c>docs</c> without <c>.md</c> (<c>configuration</c>).</summary>
    public const string Docs = Host + "/docs/";
}
