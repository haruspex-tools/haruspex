using Haruspex.Collections;
using Haruspex.Ir;

namespace Haruspex.Tests.Inference;

/// <summary>
/// The JSON constructors (<c>json_object</c>, <c>json_array</c>, <c>json_arrayagg</c>, <c>json_objectagg</c>) and the
/// native <c>json</c> type. Every rule here was observed on SQL Server 2025 first (JsonConstructorObservationTests in
/// the differential tests), and each refusal is a construct whose shape the SQL alone does not settle.
/// </summary>
public partial class JsonConstructorTests
{
    private const string Tables = """
        CREATE TABLE dbo.t (id int NOT NULL PRIMARY KEY, name nvarchar(10) NULL, code int NOT NULL, label nvarchar(20) NOT NULL);
        GO
        CREATE TABLE dbo.u (id int NOT NULL PRIMARY KEY, t_id int NOT NULL, qty smallint NULL);
        GO
        CREATE TABLE dbo.j (id int NOT NULL PRIMARY KEY, doc json NULL, body json NOT NULL);
        GO
        """;

    private static IrDocument Procedure(string body, string parameters = "") =>
        InferProcedure.Infer("*.*", ("tables.sql", Tables), ("p.sql", $"CREATE PROCEDURE dbo.p {parameters} AS\n{body}"));

    /// <summary>The field <paramref name="name"/> of the first result's row.</summary>
    private static IrField Field(IrDocument doc, string name) => Fields(doc, Row(doc)).Single(f => f.Name == name);

    private static IrRef Row(IrDocument doc) => doc.Objects[0].Results![0].Type switch
    {
        IrArray { Items: IrRef r } => r,
        IrRef r => r,
        var other => throw new InvalidOperationException($"Unexpected result type {other}."),
    };

    // Stricter than IrNavigation.Fields: the type must be an object type, not an array of one.
    private static EquatableArray<IrField> Fields(IrDocument doc, IrType type) => doc.Types[Assert.IsType<IrRef>(type).Name].Fields;

    private static IrField Sub(IrDocument doc, IrField field, string name) => Fields(doc, field.Type).Single(f => f.Name == name);

    private static IEnumerable<IrNullRule> Rules(IEnumerable<IrNullReason> reasons) => reasons.Select(r => r.Rule);
}
