using Haruspex.Cli.Output;
using Haruspex.Config;
using Haruspex.Diagnostics;
using Haruspex.Ir;

namespace Haruspex.Cli;

/// <summary>
/// <c>haruspex explain</c>: why a field is what it is, read from the committed IR alone, since an application's repository has the
/// IR and often not the SQL. With a field, its derivation: the columns it is read from, each rule that acted on it with where in
/// the SQL it applied, and the page each code has. Without one, every field of the object that is a surprise, a line each.
/// Why it reads the IR: docs/decisions/0078-the-ir-says-where-each-field-came-from.md; what it says:
/// docs/decisions/0079-a-surprise-is-explained-where-it-is-written.md.
/// </summary>
internal static class Explain
{
    /// <summary>Runs the command.</summary>
    /// <param name="target">The object (its id, such as <c>shop.get_order</c>, or its generated name), or a type's name.</param>
    /// <param name="field">The field's path, a parameter (<c>@order_id</c>), or null for the object's surprises.</param>
    /// <param name="ir">The IR file given, or null to take the config file's output folder's.</param>
    /// <param name="config">The config file given, or null to find it as <c>generate</c> does.</param>
    /// <param name="json">Whether to write JSON for tools rather than text.</param>
    /// <returns>The exit code: 0; 1 when the IR can't be read; 2 when what is named isn't in it.</returns>
    public static int Run(string target, string? field, string? ir, string? config, bool json, TextWriter output, TextWriter error, string workingDirectory)
    {
        var request = new RunRequest(null, config, [], null, workingDirectory);
        var path = ir is not null
            ? Path.GetFullPath(Path.Combine(workingDirectory, ir))
            : ModuleRun.OutOf(request) is { } folder ? Path.Join(folder, HaruspexCli.IrFileName) : null;
        if (path is null)
            return Refused(error, CliCodes.ExplainNoIr.At(null, $"no IR to read: name it with --ir, or run where {HaruspexConfig.FileName} names the output folder that holds it."), HaruspexCli.ExitCodes.Usage);

        var shown = DisplayPath.From(workingDirectory, path);
        var read = EarlierOutput.ReadIr(path);
        if (read is not EarlierIr.Readable { Document: var document })
        {
            var problem = read switch
            {
                EarlierIr.OtherVersion { Version: var version } => $"{shown} is IR {version}, and this haruspex reads IR {EnglishList.Of([.. IrDocument.ReadableVersions])}. Run haruspex generate to write it again.",
                EarlierIr.Unreadable { Problem: var why } => $"{shown} can't be read as IR: {why}",
                _ => $"no IR at {shown}: run haruspex generate first, or name the file with --ir.",
            };
            return Refused(error, CliCodes.ExplainNoIr.At(null, problem), HaruspexCli.ExitCodes.Failed);
        }

        var writer = new ExplainWriter(document, new ExplainSources(document, ModuleRun.ConfigOf(request), workingDirectory), json);
        var obj = document.Objects.FirstOrDefault(o => o.Id == target) ?? document.Objects.FirstOrDefault(o => o.Name == target);
        if (obj is null && !document.Types.ContainsKey(target))
        {
            return Refused(error, CliCodes.ExplainNotFound.At(null, $"{target} is neither an object nor a type in {shown}. Name a procedure as schema.name, a query file by its path, or a generated type by its name."), HaruspexCli.ExitCodes.Usage);
        }

        if (obj is null)
        {
            // A type that belongs to no one object, such as a recursive function's node, is named by its own name.
            var type = document.Types[target];
            var own = field is null ? null : type.Fields.FirstOrDefault(f => f.Name == field);
            if (field is not null && own is null)
                return Refused(error, CliCodes.ExplainNotFound.At(null, $"{target} has no field {field}; its fields are {EnglishList.Of([.. type.Fields.Select(f => f.Name)])}."), HaruspexCli.ExitCodes.Usage);
            writer.Type(target, own, output);
            return HaruspexCli.ExitCodes.Success;
        }

        if (field is null)
        {
            writer.Object(obj, output);
            return HaruspexCli.ExitCodes.Success;
        }

        if (field.StartsWith('@'))
        {
            var parameter = obj.Parameters.FirstOrDefault(p => string.Equals(p.Name, field, StringComparison.OrdinalIgnoreCase));
            if (parameter is null)
                return Refused(error, CliCodes.ExplainNotFound.At(null, $"{obj.Id} has no parameter {field}{(obj.Parameters.Count > 0 ? $"; its parameters are {EnglishList.Of([.. obj.Parameters.Select(p => p.Name)])}" : "")}."), HaruspexCli.ExitCodes.Usage);
            writer.Parameter(obj, parameter, output);
            return HaruspexCli.ExitCodes.Success;
        }

        if (IrFieldPlaces.Find(document, obj, field) is not { } place)
        {
            var known = IrFieldPlaces.Fields(document, obj).Select(p => p.Path).ToList();
            return Refused(error, CliCodes.ExplainNotFound.At(null, $"{obj.Id} has no field {field}{(known.Count > 0 ? $"; its fields are {EnglishList.Of([.. known])}" : "")}."), HaruspexCli.ExitCodes.Usage);
        }

        writer.Field(obj.Id, place, output);
        return HaruspexCli.ExitCodes.Success;
    }

    private static int Refused(TextWriter error, Diagnostic diagnostic, int exitCode)
    {
        error.WriteLine(diagnostic);
        return exitCode;
    }
}
