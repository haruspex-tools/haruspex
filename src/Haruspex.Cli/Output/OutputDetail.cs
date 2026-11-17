namespace Haruspex.Cli.Output;

/// <summary>How much a text report says (<c>--quiet</c>, the default, <c>--verbose</c>).</summary>
internal enum OutputDetail
{
    /// <summary>Errors only: for a script that needs nothing but the exit code and, on failure, why.</summary>
    Quiet,

    /// <summary>
    /// The summary, then every error and warning in full: what a developer running the command many times a day needs, and
    /// nothing they have already seen. Notes are counted, not listed.
    /// </summary>
    Normal,

    /// <summary>As <see cref="Normal"/>, then every note, the unknowns grouped by the object they are in.</summary>
    Verbose,
}
