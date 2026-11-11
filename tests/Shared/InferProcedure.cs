using Haruspex.Cli;
using Haruspex.Diagnostics;
using Haruspex.Ir;

namespace Haruspex.TestSupport;

/// <summary>
/// Infers procedures from SQL a test writes out: how a unit test of the inference states its input. Each test class keeps
/// its own invented tables and wraps this with them, so a test reads as the body of one procedure.
/// </summary>
internal static class InferProcedure
{
    /// <summary>
    /// Writes each file into a temporary folder, reads the folder as haruspex reads a database project (failing the test on
    /// any diagnostic, and listing them), and infers the procedures <paramref name="include"/> selects, such as <c>dbo.p</c> or <c>*.*</c>.
    /// </summary>
    public static IrDocument Infer(string include, params (string File, string Sql)[] files) => Infer(include, warnings: null, files);

    /// <summary>As the other overload, collecting the inference's warnings in <paramref name="warnings"/>.</summary>
    public static IrDocument Infer(string include, ICollection<Diagnostic>? warnings, params (string File, string Sql)[] files)
    {
        using var dir = new TempDirectory();
        foreach (var (file, sql) in files)
            dir.File(file, sql);

        var project = SqlProject.Load(dir.Path);
        Assert.True(project.Diagnostics.Count == 0, string.Join("\n", project.Diagnostics));
        return project.Infer(project.Select([ObjectPattern.Parse(include)]).Procedures, warnings);
    }
}
