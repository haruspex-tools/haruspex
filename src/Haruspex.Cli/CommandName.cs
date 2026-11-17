namespace Haruspex.Cli;

/// <summary>The commands that make a module, whose reports share one shape (<see cref="ModuleReport"/>).</summary>
internal enum CommandName
{
    /// <summary><c>haruspex generate</c>.</summary>
    Generate,

    /// <summary><c>haruspex check</c>.</summary>
    Check,
}
