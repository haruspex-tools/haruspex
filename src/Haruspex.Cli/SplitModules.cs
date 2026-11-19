using Haruspex.Collections;
using Haruspex.Config;
using Haruspex.Diagnostics;
using Haruspex.Emit.JsonSchema;
using Haruspex.Emit.Mssql;
using Haruspex.Emit.Zod;
using Haruspex.Ir;

namespace Haruspex.Cli;

/// <summary>
/// The files of a module whose config file splits its output (<c>split</c>): each emitter's files for each module, named after
/// it (<c>sales.zod.ts</c>, <c>sales.mssql.ts</c>, <c>sales.jsonschema.json</c>), and the shared ones (<c>shared.zod.ts</c>,
/// <c>shared.jsonschema.json</c>) when two or more modules share a definition. The IR stays one file, and so does the mapping
/// helper, which is the same for every module; <c>check</c> compares every file as it compares one module's
/// (<c>docs/decisions/0074-a-config-may-split-its-output-into-modules.md</c>).
/// </summary>
internal static class SplitModules
{
    /// <summary>
    /// Which module each object of <paramref name="document"/> is in (<see cref="ModuleAssignment"/>): empty when the config file
    /// doesn't split its output, with a warning for each query file whose module comment then does nothing.
    /// </summary>
    public static Outcome<EquatableMap<string, string>> Assigned(HaruspexConfig config, SqlProject project, IrDocument document)
    {
        var ids = document.Objects.Select(o => o.Id).ToHashSet(StringComparer.Ordinal);
        var assigned = ModuleAssignment.Assign(config, project.Procedures.Where(p => ids.Contains(p.ToString())), project.Queries.Where(q => ids.Contains(q.Path)));

        // A query file's comment is placed by its path from the config file's folder, as every problem of a query file is said.
        Diagnostic Under(Diagnostic d) => d.At is SourceLocation at ? d with { At = at with { File = Path.Join(config.Directory, at.File) } } : d;
        return assigned switch
        {
            Succeeded<EquatableMap<string, string>> succeeded => new Succeeded<EquatableMap<string, string>>(succeeded.Value, [.. succeeded.Diagnostics.Select(Under)]),
            Failed<EquatableMap<string, string>> failed => new Failed<EquatableMap<string, string>>([.. failed.Diagnostics.Select(Under)]),
        };
    }

    /// <summary>Each emitter's files, in the config file's order of emitters, each emitter's modules in ordinal order and then the shared file.</summary>
    public static Outcome<EquatableArray<EmittedFile>> Emit(HaruspexConfig config, IrDocument document, IrModules modules)
    {
        var strict = config.Objects == ObjectsPolicy.Strict;
        var files = config.Emitters.Select(emitter => emitter switch
        {
            Emitter.Zod => Named(ZodEmitter.Emit(document, new ZodOptions { Objects = strict ? ZodObjects.Strict : ZodObjects.Strip, SharedModule = ZodSpecifier(IrModules.Shared) }, modules), ZodSuffix),
            Emitter.JsonSchema => Named(JsonSchemaEmitter.Emit(document, new JsonSchemaOptions { Objects = strict ? JsonSchemaObjects.Strict : JsonSchemaObjects.Strip, SharedDocument = IrModules.Shared + JsonSchemaSuffix }, modules), JsonSchemaSuffix),
            Emitter.Mssql => Named(
                MssqlEmitter.Emit(document, new MssqlOptions { ServerOnly = config.ServerOnly, IsReserved = TypeScriptNames.IsReserved, RefusesExtraResultSets = strict }, modules, ZodSpecifier),
                MssqlSuffix),
            Emitter.Mapping => new Succeeded<EquatableArray<EmittedFile>>([new EmittedFile(HaruspexCli.MappingFileName, MappingModule.Text)]),
        });
        return Outcome.All(files).Select(each => (EquatableArray<EmittedFile>)[.. each.SelectMany(f => f)]);
    }

    /// <summary>What follows a module's name in its Zod file's: <c>sales.zod.ts</c>, as <see cref="HaruspexCli.ZodFileName"/> follows <c>haruspex</c>.</summary>
    private const string ZodSuffix = ".zod.ts";

    private const string JsonSchemaSuffix = ".jsonschema.json";

    private const string MssqlSuffix = ".mssql.ts";

    /// <summary>How a module's Zod file is imported from beside it, with the <c>.js</c> an ECMAScript module needs (<see cref="HaruspexCli.ZodModuleSpecifier"/>).</summary>
    private static string ZodSpecifier(string module) => $"./{module}.zod.js";

    /// <summary>An emitter's files by module, each named after its module, the modules in ordinal order and the shared file last.</summary>
    private static Outcome<EquatableArray<EmittedFile>> Named(Outcome<EquatableMap<string, string>> emitted, string suffix) =>
        emitted.Select(texts => (EquatableArray<EmittedFile>)
        [
            .. texts.OrderBy(t => t.Key == IrModules.Shared).ThenBy(t => t.Key, StringComparer.Ordinal).Select(t => new EmittedFile(t.Key + suffix, t.Value)),
        ]);
}
