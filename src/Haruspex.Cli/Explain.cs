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
    /// <param name="build">The build that runs, which finding the config file and its output folder doesn't use.</param>
    /// <returns>The exit code: 0; 1 when the IR can't be read; 2 when what is named isn't in it.</returns>
    public static int Run(string target, string? field, string? ir, string? config, bool json, TextWriter output, TextWriter error, string workingDirectory, IrBuild build)
    {
        var request = new RunRequest([], config, [], null, workingDirectory) { Build = build };
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
        var named = Named(document, target);
        if (named.Count > 1)
            return Refused(error, CliCodes.ExplainNotFound.At(null, $"{target} names {EnglishList.Of([.. named.Select(o => o.Id)])}, which differ only in case. Name one as it is written."), HaruspexCli.ExitCodes.Usage);
        var obj = named.SingleOrDefault();
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

    /// <summary>
    /// The objects <paramref name="target"/> names: the one whose id or generated name it is, as written; else those it names as
    /// SQL Server reads a name, its parts in brackets or not (<c>[shop].[order_summary]</c>) and in any case, which may be several
    /// where two differ only in case.
    /// </summary>
    private static List<IrObject> Named(IrDocument document, string target)
    {
        if (document.Objects.FirstOrDefault(o => o.Id == target || o.Name == target) is { } exact)
            return [exact];
        var wanted = WithoutBrackets(target);
        return [.. document.Objects.Where(o => string.Equals(WithoutBrackets(o.Id), wanted, StringComparison.OrdinalIgnoreCase) || string.Equals(o.Name, target, StringComparison.OrdinalIgnoreCase))];
    }

    /// <summary>A name with each part's brackets taken off (<c>[shop].[order]]s]</c> is <c>shop.order]s</c>).</summary>
    private static string WithoutBrackets(string name)
    {
        var text = new System.Text.StringBuilder(name.Length);
        var bracketed = false;
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (!bracketed && c == '[')
            {
                bracketed = true;
            }
            else if (bracketed && c == ']')
            {
                if (i + 1 < name.Length && name[i + 1] == ']')
                    text.Append(name[++i]);
                else
                    bracketed = false;
            }
            else
            {
                text.Append(c);
            }
        }

        return text.ToString();
    }

    private static int Refused(TextWriter error, Diagnostic diagnostic, int exitCode)
    {
        // The message may quote names from the IR, which may hold a control character.
        error.WriteLine(Visible.Of(diagnostic.ToString()));
        return exitCode;
    }
}
