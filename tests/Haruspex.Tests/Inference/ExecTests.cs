using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

/// <summary>
/// <c>EXEC</c> followed into procedures of the project: a callee's result sets spliced into its caller's, what a
/// callee does to its caller's temporary tables, <c>INSERT … EXEC</c>, recursion between procedures, and the calls that stay
/// unknown. The engine rules each test leans on are observed in the differential tests' <c>ExecObservationTests</c>.
/// </summary>
public partial class ExecTests
{
    private const string Tables = """
        CREATE TABLE dbo.t (id int NOT NULL PRIMARY KEY, name nvarchar(10) NULL, code int NOT NULL);
        GO
        CREATE TABLE dbo.item (id int IDENTITY(1, 1) NOT NULL PRIMARY KEY, qty int NULL);
        GO
        """;

    /// <summary>Whether a field may be null or is unknown: anything but a claim that it is never null.</summary>
    private static bool ClaimsNothingNonNull(IrField field) => field.Nullable || field.Type is IrUnknownType;

    /// <summary>Infers every procedure of the files, over the standard tables.</summary>
    private static IrDocument Infer(params (string Name, string Sql)[] procedures) => Infer("*.*", procedures);

    /// <summary>Infers the procedures <paramref name="include"/> selects.</summary>
    private static IrDocument Infer(string include, params (string Name, string Sql)[] procedures) =>
        InferProcedure.Infer(include, [("tables.sql", Tables), .. procedures.Select(p => ($"{p.Name}.sql", p.Sql))]);

    // By id, dbo.p unless named, where IrNavigation's take the first procedure: these tests infer callers and callees together.
    private static IReadOnlyList<IrResult> Results(IrDocument doc, string id = "dbo.p") =>
        Object(doc, id).Results ?? throw new InvalidOperationException("The results are unknown: " + string.Join("; ", doc.Unknowns.Where(u => u.Object == id).Select(u => u.Detail)));

    private static IrUnknown ResultsUnknown(IrDocument doc, string id = "dbo.p")
    {
        Assert.Null(Object(doc, id).Results);
        return Assert.Single(doc.Unknowns, u => u.Object == id && u.Path == "results");
    }

    private static IrField Field(IrDocument doc, string name, int result = 0, string id = "dbo.p") =>
        Assert.Single(Fields(doc, Results(doc, id)[result].Type), f => f.Name == name);
}
