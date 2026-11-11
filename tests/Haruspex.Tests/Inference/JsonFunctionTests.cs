using Haruspex.Ir;

namespace Haruspex.Tests.Inference;

/// <summary>
/// Scalar functions that return JSON, and functions that call themselves. Every rule here was observed on SQL Server
/// 2025 first (JsonFunctionObservationTests in the differential tests): a plain call is escaped text, json_query over
/// it or a json return type embeds it live, FOR JSON over no rows returns NULL, and a key whose json_query is NULL is
/// left out of FOR JSON PATH.
/// </summary>
public partial class JsonFunctionTests
{
    private const string Tables = """
        CREATE TABLE dbo.t (id int NOT NULL PRIMARY KEY, parent_id int NULL, name nvarchar(10) NOT NULL, note nvarchar(20) NULL);
        GO
        """;

    private static IrDocument Infer(string functions, string body) =>
        InferProcedure.Infer("dbo.p", ("tables.sql", Tables), ("functions.sql", functions), ("p.sql", $"CREATE PROCEDURE dbo.p AS\n{body}"));

    private static IrField Field(IrDocument doc, string name) => Fields(doc, Row(doc)).Single(f => f.Name == name);

    private static IrRef Row(IrDocument doc) => doc.Objects[0].Results![0].Type switch
    {
        IrArray { Items: IrRef r } => r,
        IrRef r => r,
        var other => throw new InvalidOperationException($"Unexpected result type {other}."),
    };

    // Stricter than IrNavigation.Fields: the type must be an object type, not an array of one.
    private static IReadOnlyList<IrField> Fields(IrDocument doc, IrType type) => doc.Types[Assert.IsType<IrRef>(type).Name].Fields;

    private static IrField Sub(IrDocument doc, IrType type, string name) => Fields(doc, type).Single(f => f.Name == name);

    private static IEnumerable<string> Rules(IEnumerable<IrNullReason> reasons) => reasons.Select(r => r.Rule);

    private const string Children = """
        CREATE FUNCTION dbo.children(@id int) RETURNS nvarchar(max) AS
        BEGIN
            RETURN (SELECT c.id, c.name FROM dbo.t AS c WHERE c.parent_id = @id FOR JSON PATH);
        END
        GO
        """;
}
