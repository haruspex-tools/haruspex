using Haruspex.Ir;

namespace Haruspex.Config;

/// <summary>The names a module may have, which name its files, in the config file, a schema or a query file's comment.</summary>
public static class ModuleNames
{
    /// <summary>Why <paramref name="name"/> can't name a module, as a sentence; null when it can.</summary>
    public static string? Problem(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length == 0 || !char.IsAsciiLetter(name[0]) || !name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
            return "a module's name starts with a letter and has only letters, digits, '_' and '-', since it names the module's files.";

        // The shared file is named so, and a file system that ignores case would take Shared for it too.
        return string.Equals(name, IrModules.Shared, StringComparison.OrdinalIgnoreCase)
            ? "it is the name of the files that hold what modules share. Name the module otherwise."
            : null;
    }
}
