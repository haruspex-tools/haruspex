using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Inference;

namespace Haruspex.Cli.Output;

/// <summary>
/// A run's report as one JSON document on standard output (<c>--format json</c>), for tools: an editor's extension, a script, a
/// dashboard. Its shape is specified by schemas/haruspex.output.schema.json, which each document names, and is versioned there:
/// a field may be added, but one is removed or changes meaning only with a new version. Everything the run found is in it,
/// whatever the text's detail; the paths are those the text report shows. Written with <see cref="Utf8JsonWriter"/>, which
/// needs no reflection, so Native AOT compiles it as it is.
/// </summary>
/// <param name="workingDirectory">The folder paths are shown from.</param>
internal sealed class JsonReport(string workingDirectory)
{
    /// <summary>The schema each document names.</summary>
    public const string Schema = "https://github.com/sharpblue/haruspex/raw/main/schemas/haruspex.output.schema.json";

    /// <summary>The format's version, which a tool checks.</summary>
    public const int Version = 1;

    /// <summary>Writes the report of <paramref name="run"/> to <paramref name="output"/>, ending with a line break.</summary>
    public void Write(RunReport run, TextWriter output)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, Options))
        {
            json.WriteStartObject();
            json.WriteString("$schema", Schema);
            json.WriteNumber("version", Version);
            json.WriteString("command", Name(run.Command));
            json.WriteNumber("exitCode", run.ExitCode);
            json.WriteStartArray("refused");
            foreach (var refused in run.Refused)
                Diagnostic(json, refused);
            json.WriteEndArray();
            json.WriteStartArray("modules");
            foreach (var module in run.Modules)
                Module(json, module);
            json.WriteEndArray();
            json.WriteEndObject();
        }

        output.Write(Encoding.UTF8.GetString(buffer.ToArray()));
        output.Write('\n');
    }

    /// <summary>
    /// Indented with LF on every platform, as every file haruspex writes is, and with non-ASCII characters as they are rather than
    /// escaped, so a name in another script reads as itself.
    /// </summary>
    private static readonly JsonWriterOptions Options = new()
    {
        Indented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private void Module(Utf8JsonWriter json, ModuleReport module)
    {
        json.WriteStartObject();
        Path(json, "config", module.Config);
        Path(json, "out", module.Out);
        json.WriteNumber("exitCode", module.ExitCode);
        if (module.Document is { } document)
        {
            json.WriteStartObject("typed");
            var procedures = document.Objects.Count(o => o.Kind == IrObjectKind.Procedure);
            json.WriteNumber("procedures", procedures);
            json.WriteNumber("queryFiles", document.Objects.Count - procedures);
            json.WriteEndObject();
        }
        else
        {
            json.WriteNull("typed");
        }

        json.WriteStartArray("written");
        foreach (var path in module.Written)
            json.WriteStringValue(Shown(path));
        json.WriteEndArray();
        Unknowns(json, module);
        Since(json, module.Since);
        json.WriteStartArray("diagnostics");
        foreach (var diagnostic in module.Diagnostics)
            Diagnostic(json, diagnostic);
        json.WriteEndArray();
        json.WriteEndObject();
    }

    private static void Unknowns(Utf8JsonWriter json, ModuleReport module)
    {
        if (module.Document is not { } document)
        {
            json.WriteNull("unknowns");
            return;
        }

        json.WriteStartObject("unknowns");
        json.WriteNumber("count", document.Unknowns.Count);
        json.WriteStartArray("byReason");
        foreach (var reason in module.Unknowns.GroupBy(u => u.Unknown.Reason).OrderByDescending(g => g.Count()).ThenBy(g => IrWireNames.Of(g.Key), StringComparer.Ordinal))
        {
            json.WriteStartObject();
            json.WriteString("reason", IrWireNames.Of(reason.Key));
            Code(json, InferenceCodes.Of(reason.Key));
            json.WriteNumber("count", reason.Count());
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    private static void Since(Utf8JsonWriter json, SinceLastRun? since)
    {
        switch (since)
        {
            case SinceLastRun.Compared compared:
                json.WriteStartObject("sinceLastRun");
                json.WriteBoolean("compared", true);
                json.WriteStartArray("changes");
                foreach (var change in compared.Claims)
                {
                    json.WriteStartObject();
                    json.WriteString("kind", Kebab(change.Kind.ToString()));
                    json.WriteString("object", change.Object);
                    json.WriteString("path", change.Path);
                    json.WriteString("before", change.Before);
                    json.WriteString("after", change.After);
                    json.WriteString("text", change.ToString());
                    json.WriteEndObject();
                }

                json.WriteEndArray();
                json.WriteStartArray("newUnknowns");
                foreach (var added in compared.New)
                    Unknown(json, added.Unknown);
                json.WriteEndArray();
                json.WriteStartArray("resolvedUnknowns");
                foreach (var resolved in compared.Resolved)
                    Unknown(json, resolved);
                json.WriteEndArray();
                json.WriteNumber("unchangedUnknowns", compared.Unchanged);
                json.WriteStartArray("rewritten");
                foreach (var file in compared.Rewritten)
                    json.WriteStringValue(file);
                json.WriteEndArray();
                json.WriteEndObject();
                break;
            case SinceLastRun.NotCompared { Why: var why }:
                json.WriteStartObject("sinceLastRun");
                json.WriteBoolean("compared", false);
                json.WriteString("why", why);
                json.WriteEndObject();
                break;
            case SinceLastRun.FirstRun:
                json.WriteStartObject("sinceLastRun");
                json.WriteBoolean("compared", false);
                json.WriteString("why", "there was no earlier output");
                json.WriteEndObject();
                break;
            default:
                json.WriteNull("sinceLastRun");
                break;
        }
    }

    private static void Unknown(Utf8JsonWriter json, IrUnknown unknown)
    {
        json.WriteStartObject();
        json.WriteString("object", unknown.Object);
        json.WriteString("path", unknown.Path);
        json.WriteString("reason", IrWireNames.Of(unknown.Reason));
        Code(json, InferenceCodes.Of(unknown.Reason));
        json.WriteEndObject();
    }

    private static void Code(Utf8JsonWriter json, DiagnosticCode? code)
    {
        if (code is null)
            json.WriteNull("code");
        else
            json.WriteString("code", code.ToString());
    }

    private void Diagnostic(Utf8JsonWriter json, Diagnostic diagnostic)
    {
        var (_, _, text) = DiagnosticFormatter.Parts(diagnostic);
        var (held, line, column) = Place.Of(diagnostic);
        var file = held is null ? null : Shown(held);
        json.WriteStartObject();
        json.WriteString("code", diagnostic.Code.ToString());
        json.WriteString("severity", DiagnosticFormatter.Word(diagnostic.Severity));
        json.WriteString("message", text);
        json.WriteString("file", file);
        WriteNumberOrNull(json, "line", line);
        WriteNumberOrNull(json, "column", column);
        json.WriteString("help", DiagnosticPages.Of(diagnostic.Code));
        json.WriteEndObject();
    }

    private static void WriteNumberOrNull(Utf8JsonWriter json, string name, int? value)
    {
        if (value is { } number)
            json.WriteNumber(name, number);
        else
            json.WriteNull(name);
    }

    private void Path(Utf8JsonWriter json, string name, string? path) => json.WriteString(name, path is null ? null : Shown(path));

    private string Shown(string path) => DisplayPath.From(workingDirectory, path);

    private static string Name(CommandName command) => command switch
    {
        CommandName.Generate => "generate",
        CommandName.Check => "check",
    };

    /// <summary>A name in Pascal case as the IR writes its own names, in lower case with hyphens: <c>BecameNullable</c>, <c>became-nullable</c>.</summary>
    internal static string Kebab(string pascal)
    {
        var kebab = new StringBuilder();
        foreach (var c in pascal)
        {
            if (char.IsUpper(c) && kebab.Length > 0)
                kebab.Append('-');
            kebab.Append(char.ToLowerInvariant(c));
        }

        return kebab.ToString();
    }
}
