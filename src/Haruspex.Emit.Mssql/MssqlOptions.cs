namespace Haruspex.Emit.Mssql;

/// <summary>
/// Settings for the mssql emitter. The emitter never reads the config file: whoever composes the tool (the CLI) maps the
/// config's settings to these.
/// </summary>
public sealed record MssqlOptions
{
    /// <summary>
    /// The module the functions import their schemas and types from, as an import specifier: <c>./haruspex.zod.js</c>, the Zod module
    /// the CLI writes beside them. It must declare the names <see cref="SchemaModuleNames"/> gives.
    /// </summary>
    public string SchemaModule { get; init; } = "./haruspex.zod.js";

    /// <summary>
    /// Whether the module starts with <c>import 'server-only'</c>, which makes a Next.js build fail where browser code imports it.
    /// Off by default: haruspex assumes no framework.
    /// </summary>
    public bool ServerOnly { get; init; }

    /// <summary>
    /// Whether a function refuses a result set after those its procedure lists, as <c>"objects": "strict"</c> asks. Off by
    /// default: a function reads the sets it knows and ignores any after them, so a deployed application survives its database
    /// gaining a trailing set, as it survives a result gaining a key
    /// (<c>docs/decisions/0063-a-function-ignores-result-sets-after-those-it-knows.md</c>). A set every call returns that is
    /// missing fails either way.
    /// </summary>
    public bool RefusesExtraResultSets { get; init; }

    /// <summary>
    /// The words the target language keeps for itself, which no function may be named: TypeScript's, which the CLI hands over
    /// from the Zod emitter, as it hands them to the config's naming. Nothing is reserved by default.
    /// </summary>
    public Func<string, bool> IsReserved { get; init; } = _ => false;
}
