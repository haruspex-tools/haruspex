using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

/// <summary>
/// <c>CROSS APPLY</c>, <c>OUTER APPLY</c>, <c>VALUES</c> lists and views. The engine rules each test leans on are
/// observed in the differential tests' <c>ApplyViewObservationTests</c> (and <c>RefinementObservationTests</c> for the
/// aggregate and cardinality rules reused here), named where they matter.
/// </summary>
public partial class ApplyViewTests
{
    private const string Tables = """
        CREATE TABLE dbo.t (id int NOT NULL PRIMARY KEY, name nvarchar(10) NULL, n int NULL, code varchar(10) NOT NULL);
        GO
        CREATE TABLE dbo.u (id int NOT NULL PRIMARY KEY, t_id int NOT NULL, note nvarchar(10) NULL, label nvarchar(10) NOT NULL);
        GO
        CREATE TABLE dbo.pair (a int NOT NULL, b int NOT NULL, v int NULL, CONSTRAINT pk_pair PRIMARY KEY (a, b));
        GO
        """;

    private static IrDocument Infer(params (string File, string Sql)[] files) => InferProcedure.Infer("dbo.p", files);

    private static IrDocument Procedure(string body, string parameters = "", string views = "") =>
        views.Length == 0
            ? Infer(("tables.sql", Tables), ("p.sql", $"CREATE PROCEDURE dbo.p {parameters} AS\n{body}"))
            : Infer(("tables.sql", Tables), ("views.sql", views), ("p.sql", $"CREATE PROCEDURE dbo.p {parameters} AS\n{body}"));

    // Stricter than IrNavigation.Field: asserts the procedure has exactly one result. Rules reads it, so it is kept too.
    private static IrField Field(IrDocument doc, string name) => Assert.Single(Fields(doc, Assert.Single(Results(doc)).Type), f => f.Name == name);

    private static string[] Rules(IrDocument doc, string name) => [.. Field(doc, name).NullableBecause.Select(r => r.Rule)];

    // IrNavigation.AllTyped, under the name these tests use.
    private static void Typed(IrDocument doc) => AllTyped(doc);

    private static IrCardinality Rows(IrDocument doc) => Assert.Single(Results(doc)).Cardinality;
}
