namespace Haruspex.Config;

/// <summary>Whether a problem stops generation.</summary>
public enum ConfigSeverity
{
    /// <summary>Nothing is generated.</summary>
    Error,

    /// <summary>Generation goes ahead; the config file could be simpler (an override the inference now agrees with).</summary>
    Warning,
}
