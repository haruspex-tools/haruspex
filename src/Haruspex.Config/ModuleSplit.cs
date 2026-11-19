using Haruspex.Collections;
using Haruspex.Diagnostics;

namespace Haruspex.Config;

/// <summary>
/// How the config file splits its output into modules (<c>split</c>): a module per SQL schema, or the modules it lists, each with
/// the schemas whose procedures it holds. Each module is the files one config writes, named after the module, with what two or
/// more modules share in a shared file (<c>docs/decisions/0074-a-config-may-split-its-output-into-modules.md</c>).
/// </summary>
/// <param name="Modules">
/// The modules listed (<c>"split": { "sales": ["sales", "shop"] }</c>), in the order written; null for <c>"split": "schema"</c>,
/// a module per schema of the selected procedures, named as the schema is.
/// </param>
/// <param name="At">Where <c>split</c> is written.</param>
public sealed record ModuleSplit(EquatableArray<SplitModule>? Modules, JsonLocation At)
{
    /// <summary>
    /// The module a query file is in when it names none (<c>-- @module</c>): its name says what it holds, and a list may name it
    /// to give it schemas too, or a query file may name another.
    /// </summary>
    public const string QueriesModule = "queries";

    /// <summary>Whether each schema is a module of its own (<c>"split": "schema"</c>).</summary>
    public bool BySchema => Modules is null;
}
