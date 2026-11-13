using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

/// <summary>
/// DML on permanent tables, <c>OUTPUT</c>, triggers, <c>WITH</c> before DML, and cursors. The engine rules each
/// test leans on are observed in the differential tests' <c>DmlObservationTests</c>, named in each test.
/// </summary>
public partial class DmlTests
{
    private const string Tables = """
        CREATE TABLE dbo.item (id int NOT NULL PRIMARY KEY, label nvarchar(10) NULL, qty int NOT NULL DEFAULT 0);
        GO
        CREATE TABLE dbo.note (id int IDENTITY(1, 1) NOT NULL PRIMARY KEY, text nvarchar(20) NULL);
        GO
        CREATE TABLE dbo.error_log (id int IDENTITY(1, 1) NOT NULL PRIMARY KEY, message nvarchar(4000) NULL, at datetime2(0) NOT NULL);
        GO
        """;

    private sealed record Inferred(IrDocument Doc, IReadOnlyList<Diagnostic> Warnings);

    /// <summary>Infers every procedure of the files, over the standard tables.</summary>
    private static Inferred Infer(params (string Name, string Sql)[] files)
    {
        var warnings = new List<Diagnostic>();
        var doc = InferProcedure.Infer("*.*", warnings, [("tables.sql", Tables), .. files.Select(f => ($"{f.Name}.sql", f.Sql))]);
        return new Inferred(doc, warnings);
    }

    private static IrDocument Doc(params (string Name, string Sql)[] files) => Infer(files).Doc;

    // By id, dbo.p unless named, where IrNavigation's take the first procedure: these tests infer callers and callees together.
    private static EquatableArray<IrResult> Results(IrDocument doc, string id = "dbo.p") =>
        Object(doc, id).Results ?? throw new InvalidOperationException("The results are unknown: " + string.Join("; ", doc.Unknowns.Where(u => u.Object == id).Select(u => u.Detail)));

    private static IrUnknown ResultsUnknown(IrDocument doc, string id = "dbo.p")
    {
        Assert.Null(Object(doc, id).Results);
        return Assert.Single(doc.Unknowns, u => u.Object == id && u.Path == "results");
    }

    private static IrField Field(IrDocument doc, string name, int result = 0, string id = "dbo.p") =>
        Assert.Single(Fields(doc, Results(doc, id)[result].Type), f => f.Name == name);
}
