namespace Haruspex.SqlServer.Sources.Projects;

/// <summary>A script a project's build reads, as it is on disk, and the settings it starts with.</summary>
/// <param name="Path">Its full path, as on disk.</param>
/// <param name="Settings">The settings it starts with: its item's, or its project's.</param>
internal sealed record ProjectScript(string Path, ScriptSettings Settings);
