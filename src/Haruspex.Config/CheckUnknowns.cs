namespace Haruspex.Config;

/// <summary>What <c>haruspex check</c> does about unknowns in the module (<c>"check": { "unknowns": … }</c>).</summary>
public enum CheckUnknowns
{
    /// <summary>Each is a note, and the check passes with them, as <c>generate</c> does. The default.</summary>
    Report,

    /// <summary>Any unknown fails the check: for a module that must be typed throughout.</summary>
    Fail,
}
