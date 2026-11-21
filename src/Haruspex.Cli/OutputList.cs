using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Haruspex.Cli;

/// <summary>
/// The list of the files <c>generate</c> wrote to an output folder, kept beside them as <c>haruspex.files.json</c>: the one thing a
/// later run trusts to say which files it may remove, so that it removes a file of its own that it no longer writes and never one
/// it didn't write (docs/decisions/0091-generate-removes-only-the-files-its-list-names.md). Each path is relative to the folder,
/// with <c>/</c> between names, and the paths are sorted, so the list is the same on every platform and diffs cleanly. It is a
/// generated file, committed with the others, and <c>check</c> compares it as it compares them.
/// </summary>
internal static class OutputList
{
    /// <summary>The list's name in the output folder.</summary>
    public const string FileName = "haruspex.files.json";

    /// <summary>
    /// What the list says of itself. JSON has no comments, so the warning every generated file starts with is a key; it says
    /// what the list is for, since a reader who finds it in a diff has no other way to know.
    /// </summary>
    private const string About = "The files haruspex generate wrote to this folder, which a later run removes when it no longer writes them. Generated: do not edit.";

    private static readonly JsonWriterOptions Options = new()
    {
        Indented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// The list's text for <paramref name="entries"/>: sorted ordinally, each once, so the same files always give the same bytes,
    /// with a line break at the end as every generated file has.
    /// </summary>
    public static string Text(IEnumerable<string> entries)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, Options))
        {
            json.WriteStartObject();
            json.WriteString("about", About);
            json.WriteStartArray("files");
            foreach (var entry in entries.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
                json.WriteStringValue(entry);
            json.WriteEndArray();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
    }

    /// <summary>The list in <paramref name="folder"/>, read, or why there is none to go by.</summary>
    public static EarlierList Read(string folder)
    {
        var path = Path.Join(folder, FileName);
        try
        {
            if (!File.Exists(path))
                return new EarlierList.Absent();
            var text = File.ReadAllText(path);
            return Entries(text) is { } entries
                ? new EarlierList.Readable([.. entries], text)
                : new EarlierList.Unreadable("it isn't an object whose \"files\" is a list of paths.");
        }
        catch (JsonException e)
        {
            // Said by where the parser stopped, as an unreadable IR is (EarlierOutput.ReadIr).
            return new EarlierList.Unreadable($"it isn't JSON, from line {e.LineNumber + 1}.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new EarlierList.Unreadable(e.Message);
        }
    }

    /// <summary>The paths <paramref name="text"/> lists under <c>"files"</c>; null when it isn't such a list.</summary>
    private static List<string>? Entries(string text)
    {
        using var document = JsonDocument.Parse(text);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("files", out var files)
            || files.ValueKind != JsonValueKind.Array
            || files.EnumerateArray().Any(f => f.ValueKind != JsonValueKind.String))
        {
            return null;
        }

        return [.. files.EnumerateArray().Select(f => f.GetString()).OfType<string>()];
    }
}
