using Haruspex.Ir;

namespace Haruspex.Cli;

/// <summary>
/// What a module's output folder holds before a run touches it: the IR, read or why it can't be, and each emitted file's text.
/// <see cref="Check"/> compares the module with it, as what is committed; <see cref="Generate"/> reads it before writing over it,
/// to say what changed since the last run. One reader, so the two commands agree on what the folder held.
/// </summary>
internal sealed class EarlierOutput
{
    private EarlierOutput(EarlierIr ir, IReadOnlyList<string?> files)
    {
        Ir = ir;
        Files = files;
    }

    /// <summary>The IR, or why there is none to compare with.</summary>
    public EarlierIr Ir { get; }

    /// <summary>The text of each of the module's files, in <see cref="EmittedModule.Files"/>' order; null for one that isn't there.</summary>
    public IReadOnlyList<string?> Files { get; }

    /// <summary>Reads the folder <paramref name="module"/> is written to.</summary>
    public static EarlierOutput Read(EmittedModule module)
    {
        var files = new List<string?>();
        try
        {
            foreach (var file in module.Files)
            {
                var path = module.PathOf(file);
                files.Add(File.Exists(path) ? File.ReadAllText(path) : null);
            }

            if (!File.Exists(module.IrPath))
                return new EarlierOutput(new EarlierIr.Absent(), files);
            var text = File.ReadAllText(module.IrPath);
            if (IrJsonReader.VersionOf(text) is { } version && !IrDocument.ReadableVersions.Contains(version, StringComparer.Ordinal))
                return new EarlierOutput(new EarlierIr.OtherVersion(version), files);
            return new EarlierOutput(new EarlierIr.Readable(IrJsonReader.Read(text)), files);
        }
        catch (IrFormatException e)
        {
            return new EarlierOutput(new EarlierIr.Unreadable(e.Message, AsIr: true), files);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new EarlierOutput(new EarlierIr.Unreadable(e.Message, AsIr: false), files);
        }
    }
}
