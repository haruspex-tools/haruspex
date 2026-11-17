using Haruspex.Ir;

namespace Haruspex.Cli;

/// <summary>
/// What a module's output folder holds before a run touches it: the IR, read or why it can't be, and each emitted file's text.
/// <see cref="Check"/> compares the module with it (in CI, what is committed); <see cref="Generate"/> reads it before writing over it,
/// to say what changed since the last run. One reader, so the two commands agree on what the folder held.
/// </summary>
internal sealed class EarlierOutput
{
    private EarlierOutput(EarlierIr ir, IReadOnlyList<string?> files, IReadOnlyDictionary<string, string> unreadable)
    {
        Ir = ir;
        Files = files;
        Unreadable = unreadable;
    }

    /// <summary>The IR, or why there is none to compare with.</summary>
    public EarlierIr Ir { get; }

    /// <summary>
    /// The text of each of the module's files, in <see cref="EmittedModule.Files"/>' order, one for each; null for one that isn't
    /// there or can't be read (<see cref="Unreadable"/> says which).
    /// </summary>
    public IReadOnlyList<string?> Files { get; }

    /// <summary>The module's files that are there but can't be read, by path, with what the file system said.</summary>
    public IReadOnlyDictionary<string, string> Unreadable { get; }

    /// <summary>
    /// Reads the folder <paramref name="module"/> is written to: each file apart, so one that can't be read is said by its own
    /// name and doesn't stop the others being read.
    /// </summary>
    public static EarlierOutput Read(EmittedModule module)
    {
        var files = new List<string?>();
        var unreadable = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in module.Files)
        {
            var path = module.PathOf(file);
            try
            {
                files.Add(File.Exists(path) ? File.ReadAllText(path) : null);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                files.Add(null);
                unreadable[path] = e.Message;
            }
        }

        return new EarlierOutput(ReadIr(module.IrPath), files, unreadable);
    }

    /// <summary>The IR at <paramref name="path"/>, or why there is none to compare with.</summary>
    private static EarlierIr ReadIr(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new EarlierIr.Absent();
            var text = File.ReadAllText(path);
            if (IrJsonReader.VersionOf(text) is { } version && !IrDocument.ReadableVersions.Contains(version, StringComparer.Ordinal))
                return new EarlierIr.OtherVersion(version);
            return new EarlierIr.Readable(IrJsonReader.Read(text));
        }
        catch (IrFormatException e)
        {
            // Text that isn't JSON is said by where the parser stopped; the parser's own account, which names its internals
            // ("Expected depth to be zero", "BytePositionInLine"), is left out.
            var problem = e.InnerException is System.Text.Json.JsonException { Message: var inner } && e.Message.EndsWith(": " + inner, StringComparison.Ordinal)
                ? e.Message[..^(inner.Length + 2)] + "."
                : e.Message;
            return new EarlierIr.Unreadable(problem, AsIr: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new EarlierIr.Unreadable(e.Message, AsIr: false);
        }
    }
}
