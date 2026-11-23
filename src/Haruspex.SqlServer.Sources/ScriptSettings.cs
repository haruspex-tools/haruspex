namespace Haruspex.SqlServer.Sources;

/// <summary>
/// The two settings a script starts with, which a definition in it keeps: whether <c>ANSI_NULLS</c> is on, which decides how a
/// comparison with NULL behaves, and whether <c>QUOTED_IDENTIFIER</c> is on, which decides whether <c>"x"</c> is a name or a string,
/// and so how the script parses.
/// </summary>
/// <remarks>
/// A folder of scripts doesn't say them, so a folder's start with both on, as a connection that sets nothing does. A <c>SET</c>
/// in a script still changes them from there on: <c>ANSI_NULLS</c> as each definition is read (<see cref="ScriptReader"/>), and
/// <c>QUOTED_IDENTIFIER</c> as each batch is parsed (<see cref="ScriptParser"/>). A project says them: its <c>AnsiNulls</c> and <c>QuotedIdentifier</c>
/// properties, and each script's item metadata of the same names (<c>Projects.ProjectFiles</c>).
/// </remarks>
/// <param name="AnsiNulls">Whether <c>ANSI_NULLS</c> is on.</param>
/// <param name="QuotedIdentifier">Whether <c>QUOTED_IDENTIFIER</c> is on.</param>
internal sealed record ScriptSettings(bool AnsiNulls, bool QuotedIdentifier)
{
    /// <summary>Both on: a folder's scripts, and a project's that sets neither.</summary>
    public static ScriptSettings Default { get; } = new(AnsiNulls: true, QuotedIdentifier: true);
}
