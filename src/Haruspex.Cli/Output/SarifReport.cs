using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Haruspex.Diagnostics;

namespace Haruspex.Cli.Output;

/// <summary>
/// A run's report as a SARIF 2.1.0 log on standard output (<c>--format sarif</c>), the OASIS standard for a static analyser's
/// results, which code-scanning services read (GitHub's among them) to show each one in place in a pull request. One run of
/// one tool, haruspex; each diagnostic code it reported is a rule, with its title and its page; each diagnostic is a result, at
/// its file, line and column. A file's place is a URI relative to the root the report is read from: the repository's, when the
/// run is in GitHub Actions (<c>GITHUB_WORKSPACE</c>), otherwise the working directory. Nothing in it depends on the machine or
/// the time, so a run gives the same bytes on every machine. tests/sarif holds the schema it is tested against.
/// </summary>
/// <param name="root">The folder each file's URI is relative to.</param>
/// <param name="version">The tool's version, which the log records.</param>
internal sealed class SarifReport(string root, string version)
{
    /// <summary>The schema a SARIF 2.1.0 log names, as the standard gives it.</summary>
    public const string Schema = "https://docs.oasis-open.org/sarif/sarif/v2.1.0/errata01/os/schemas/sarif-schema-2.1.0.json";

    /// <summary>Writes the log of <paramref name="run"/> to <paramref name="output"/>, ending with a line break.</summary>
    public void Write(RunReport run, TextWriter output)
    {
        var found = run.Refused.Select(d => (Module: (string?)null, Diagnostic: d, Why: (string?)null))
            .Concat(run.Modules.SelectMany(m => m.Diagnostics.Select(d => (Module: run.Several ? m.Config : null, Diagnostic: d, Why: (string?)null))))
            .Concat(run.Modules.SelectMany(m => m.Acknowledged.Select(a => (Module: run.Several ? m.Config : null, Diagnostic: a.Warning, Why: (string?)a.By.Why))))
            .ToList();
        var rules = found.Select(f => f.Diagnostic.Code).Distinct().OrderBy(c => c.Number).ToList();

        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, Options))
        {
            json.WriteStartObject();
            json.WriteString("$schema", Schema);
            json.WriteString("version", "2.1.0");
            json.WriteStartArray("runs");
            json.WriteStartObject();
            Tool(json, rules);
            json.WriteStartArray("invocations");
            json.WriteStartObject();
            json.WriteBoolean("executionSuccessful", run.ExitCode is HaruspexCli.ExitCodes.Success or HaruspexCli.ExitCodes.Failed);
            json.WriteNumber("exitCode", run.ExitCode);
            json.WriteEndObject();
            json.WriteEndArray();
            json.WriteStartArray("results");
            foreach (var (module, diagnostic, why) in found)
                Result(json, diagnostic, rules.IndexOf(diagnostic.Code), module, why);
            json.WriteEndArray();
            json.WriteEndObject();
            json.WriteEndArray();
            json.WriteEndObject();
        }

        output.Write(Encoding.UTF8.GetString(buffer.ToArray()));
        output.Write('\n');
    }

    private static readonly JsonWriterOptions Options = new()
    {
        Indented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private void Tool(Utf8JsonWriter json, List<DiagnosticCode> rules)
    {
        json.WriteStartObject("tool");
        json.WriteStartObject("driver");
        json.WriteString("name", "haruspex");
        json.WriteString("version", version);
        json.WriteString("informationUri", "https://github.com/sharpblue/haruspex");
        json.WriteStartArray("rules");
        foreach (var code in rules)
        {
            json.WriteStartObject();
            json.WriteString("id", code.ToString());
            json.WriteStartObject("shortDescription");
            json.WriteString("text", code.Title);
            json.WriteEndObject();
            json.WriteString("helpUri", DiagnosticPages.Of(code));
            json.WriteStartObject("defaultConfiguration");
            json.WriteString("level", DiagnosticFormatter.Word(code.Severity));
            json.WriteEndObject();
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
        json.WriteEndObject();
    }

    /// <summary>
    /// A result; a warning the config file acknowledges is one with an external suppression, SARIF's word for a result reviewed and
    /// accepted in a file beside the code, with why as its justification, so a viewer shows it as suppressed.
    /// </summary>
    private void Result(Utf8JsonWriter json, Diagnostic diagnostic, int rule, string? module, string? why)
    {
        json.WriteStartObject();
        json.WriteString("ruleId", diagnostic.Code.ToString());
        json.WriteNumber("ruleIndex", rule);
        json.WriteString("level", DiagnosticFormatter.Word(diagnostic.Severity));
        json.WriteStartObject("message");
        json.WriteString("text", DiagnosticFormatter.Parts(diagnostic).Text);
        json.WriteEndObject();
        var (file, line, column) = Place.Of(diagnostic);
        if (file is not null)
        {
            json.WriteStartArray("locations");
            json.WriteStartObject();
            json.WriteStartObject("physicalLocation");
            json.WriteStartObject("artifactLocation");
            json.WriteString("uri", Uri(file));
            json.WriteEndObject();
            if (line is { } startLine && column is { } startColumn)
            {
                json.WriteStartObject("region");
                json.WriteNumber("startLine", startLine);
                json.WriteNumber("startColumn", startColumn);
                json.WriteEndObject();
            }

            json.WriteEndObject();
            json.WriteEndObject();
            json.WriteEndArray();
        }

        if (why is not null)
        {
            json.WriteStartArray("suppressions");
            json.WriteStartObject();
            json.WriteString("kind", "external");
            json.WriteString("justification", why);
            json.WriteEndObject();
            json.WriteEndArray();
        }

        if (module is not null)
        {
            json.WriteStartObject("properties");
            json.WriteString("module", DisplayPath.From(root, module));
            json.WriteEndObject();
        }

        json.WriteEndObject();
    }

    /// <summary>
    /// A file's place as a URI reference relative to the root, each name escaped as a URI needs (a location in a DACPAC,
    /// <c>sales.dacpac/[sales].[get_order]</c>, has brackets, which a URI's path may not hold as they are); a file outside the
    /// root as a <c>file:</c> URI.
    /// </summary>
    private string Uri(string file)
    {
        var shown = DisplayPath.From(root, file);
        return Path.IsPathFullyQualified(shown)
            ? new Uri(shown).AbsoluteUri
            : string.Join('/', shown.Split('/').Select(System.Uri.EscapeDataString));
    }
}
