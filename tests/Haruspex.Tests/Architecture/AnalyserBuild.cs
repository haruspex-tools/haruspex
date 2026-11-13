using System.Reflection;

namespace Haruspex.Tests.Architecture;

/// <summary>
/// The project's own analysers as the build made them (<c>analysers/README.md</c>): the test project runs them as analysers, never
/// references them, so their assembly is read from its build folder, beside this test's configuration.
/// </summary>
internal static class AnalyserBuild
{
    /// <summary>The analysers' assembly file.</summary>
    public static string Path { get; } = System.IO.Path.Combine(
        RepositoryPaths.Root,
        "analysers",
        "Haruspex.Analysers",
        "bin",
        System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(typeof(AnalyserBuild).Assembly.Location)))!,
        "netstandard2.0",
        "Haruspex.Analysers.dll");

    /// <summary>The analysers' assembly, loaded once.</summary>
    public static Assembly Assembly { get; } = Assembly.LoadFrom(Path);
}
