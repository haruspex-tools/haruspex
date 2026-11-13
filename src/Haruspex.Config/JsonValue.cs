namespace Haruspex.Config;

// The config file as a tree that remembers where each value was written, so every error can name its line. JsonDocument
// forgets positions, so the file is read token by token with Utf8JsonReader, which also reads comments and trailing
// commas (JSONC, as tsconfig.json allows) without a dependency.

/// <summary>A value in the config file, with where it starts.</summary>
internal abstract record JsonValue
{
    /// <summary>Only this assembly derives a case, so the set is closed (docs/decisions/0031-the-model-is-closed-hierarchies-of-records.md).</summary>
    private protected JsonValue(int line, int column)
    {
        Line = line;
        Column = column;
    }

    /// <summary>The line it starts on.</summary>
    public int Line { get; }

    /// <summary>The column it starts at.</summary>
    public int Column { get; }
}
