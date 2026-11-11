using Haruspex.Ir;

namespace Haruspex.Tests.Inference;

/// <summary>
/// A procedure's statements walked in order: temporary tables, table variables, variables, control flow and
/// CTEs. The engine rules each test leans on are observed in the differential tests'
/// <c>StatementFlowObservationTests</c>, named where they matter.
/// </summary>
public partial class StatementFlowTests
{
    private const string Tables = """
        CREATE TABLE dbo.t (id int NOT NULL PRIMARY KEY, name nvarchar(10) NULL, code int NOT NULL,
            status varchar(6) NOT NULL CONSTRAINT ck_t_status CHECK (status IN ('open', 'shut')));
        GO
        CREATE TABLE dbo.u (id int NOT NULL PRIMARY KEY, t_id int NOT NULL, name nvarchar(10) NOT NULL);
        GO
        CREATE TABLE dbo.node (id int NOT NULL PRIMARY KEY, parent_id int NULL, label nvarchar(20) NOT NULL, note nvarchar(20) NULL);
        GO
        CREATE TYPE dbo.code FROM nvarchar(4) NOT NULL;
        GO
        """;

    private static IrDocument Infer(params (string File, string Sql)[] files) => InferProcedure.Infer("*.*", files);

    /// <summary>Infers <c>dbo.p</c>, whose body is <paramref name="body"/>, over the standard tables.</summary>
    private static IrDocument Procedure(string body, string parameters = "") =>
        Infer(("tables.sql", Tables), ("p.sql", $"CREATE PROCEDURE dbo.p {parameters} AS\n{body}"));

    private static IrScalar Scalar(IrField field) => Assert.IsType<IrScalar>(field.Type);
}
