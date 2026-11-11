using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

/// <summary>
/// Refinement: what predicates, joins and single-row queries prove. A filter that is never TRUE when a column is
/// NULL makes the column non-null in the rows it keeps; an aggregate without GROUP BY returns exactly one row; TOP (1),
/// FETCH 1 and a filter fixing a whole key return at most one. The engine rules each test leans on are observed in the
/// differential tests' <c>RefinementObservationTests</c>, named where they matter.
/// </summary>
public partial class RefinementTests
{
    private const string Tables = """
        CREATE TABLE dbo.t (id int NOT NULL PRIMARY KEY, name nvarchar(10) NULL, n int NULL,
            code varchar(10) NULL CONSTRAINT uq_t_code UNIQUE,
            status varchar(6) NULL CONSTRAINT ck_t_status CHECK (status IN ('open', 'shut')));
        GO
        CREATE TABLE dbo.u (id int NOT NULL PRIMARY KEY, t_id int NULL, note nvarchar(10) NULL, label nvarchar(10) NOT NULL);
        GO
        CREATE TABLE dbo.pair (a int NOT NULL, b int NOT NULL, v int NULL, w varchar(5) NULL, CONSTRAINT pk_pair PRIMARY KEY (a, b),
            CONSTRAINT uq_pair_vw UNIQUE (v, w));
        GO
        """;

    private static IrDocument Infer(params (string File, string Sql)[] files) => InferProcedure.Infer("*.*", files);

    private static IrDocument Procedure(string body, string parameters = "") =>
        Infer(("tables.sql", Tables), ("p.sql", $"CREATE PROCEDURE dbo.p {parameters} AS\n{body}"));

    private static IrCardinality Rows(string body, string parameters = "") => Assert.Single(Results(Procedure(body, parameters))).Cardinality;
}
