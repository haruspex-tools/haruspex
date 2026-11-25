using Haruspex.Cli;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;

namespace Haruspex.TestSupport;

/// <summary>
/// How a test selects a project's procedures and infers them with no config file: by patterns alone, without the schemas' blocks,
/// excludes and overrides through which the command selects (<c>ProcedureSelection</c>, tested in <c>ProcedureSelectionTests</c>).
/// </summary>
internal static class SqlProjectTestExtensions
{
    /// <summary>
    /// The project's procedures that any of <paramref name="include"/> matches. A pattern that matches nothing fails the test, since
    /// a test whose pattern was mistyped would otherwise check nothing.
    /// </summary>
    public static TestSelection Select(this SqlProject project, IReadOnlyList<ObjectPattern> include)
    {
        var unmatched = include.Where(i => !project.Procedures.Any(i.Matches)).Select(i => i.Text).ToList();
        Assert.True(unmatched.Count == 0, $"no procedure matches {string.Join(", ", unmatched)}");
        return new TestSelection([.. project.Procedures.Where(p => include.Any(i => i.Matches(p)))]);
    }

    /// <summary>Infers <paramref name="selected"/> and settles their names with the default naming rules, as with no config file.</summary>
    public static Outcome<IrDocument> Infer(this SqlProject project, IReadOnlyList<ObjectName> selected) =>
        project.Infer(selected, HaruspexConfig.None("."));
}
