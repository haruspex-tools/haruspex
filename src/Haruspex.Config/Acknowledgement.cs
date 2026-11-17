using Haruspex.Diagnostics;

namespace Haruspex.Config;

/// <summary>
/// A warning the config file says has been read and is to be lived with (<c>acknowledge</c>): one warning, named by its code and
/// its subject together, with why. A warning acknowledged is counted rather than said on each run; every other warning of the
/// same code is still said. Which codes have subjects, and so can be acknowledged, is the command line's to know; the config
/// layer reads the entry.
/// </summary>
/// <param name="Code">The warning's code, as written: <c>HAR1003</c>.</param>
/// <param name="Subject">What it is about, as the warning names it: a package's or project's file name, a target's name, an object's.</param>
/// <param name="Why">Why it is lived with, for the next reader of the file.</param>
/// <param name="At">Where the entry is written.</param>
public sealed record Acknowledgement(string Code, string Subject, string Why, JsonLocation At);
