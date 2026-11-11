using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

/// <summary>
/// Set operations (<c>UNION</c>, <c>UNION ALL</c>, <c>INTERSECT</c>, <c>EXCEPT</c>), <c>CROSS JOIN</c>, <c>FULL OUTER JOIN</c> and
/// <c>OPENJSON</c>. Each rule rests on an observation in the differential tests' <c>SetJoinObservationTests</c>, named
/// where it matters.
/// </summary>
public partial class SetJoinTests
{
    private const string Tables = """
        CREATE TABLE dbo.t (
            id int NOT NULL PRIMARY KEY, x int NULL, tiny tinyint NULL, amount decimal(9,2) NULL, code char(4) NULL,
            short varchar(10) NULL, label varchar(8) NOT NULL, big bigint NOT NULL, doc nvarchar(max) NULL,
            status varchar(10) NULL CONSTRAINT ck_status CHECK (status IN ('open', 'shut')));
        GO
        CREATE TABLE dbo.u (id int NOT NULL PRIMARY KEY, t_id int NOT NULL, qty int NOT NULL, note varchar(20) NULL);
        GO
        """;

    private static IrDocument Infer(params (string File, string Sql)[] files) => InferProcedure.Infer("dbo.p", files);

    private static IrDocument Procedure(string body, string others = "") =>
        others.Length == 0
            ? Infer(("tables.sql", Tables), ("p.sql", $"CREATE PROCEDURE dbo.p AS\n{body}"))
            : Infer(("tables.sql", Tables), ("others.sql", others), ("p.sql", $"CREATE PROCEDURE dbo.p AS\n{body}"));

    private static string Sql(IrDocument doc, string name) => Field(doc, name).Type switch
    {
        IrScalar scalar => scalar.Sql ?? "(none)",
        var other => other.GetType().Name,
    };

    /// <summary>The type and the reasons to be null, as one string: <c>varchar(6) nullable-column</c>.</summary>
    private static string Typed(IrDocument doc, string name) => string.Join(" ", [Sql(doc, name), .. Rules(doc, name)]);

    private static string? Values(IrDocument doc, string name) =>
        Field(doc, name).Type is IrScalar { Facets.Values: { } set }
#pragma warning disable CA1308 // Lower case to write the match in a description, not to compare it.
            ? $"{set.Match.ToString().ToLowerInvariant()}:{string.Join("|", set.Items.Select(i => i.Text))}"
#pragma warning restore CA1308
            : null;

    private static IrCardinality Rows(IrDocument doc) => Results(doc)[0].Cardinality;
}
