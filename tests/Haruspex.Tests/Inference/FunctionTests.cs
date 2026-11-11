using Haruspex.Ir;

namespace Haruspex.Tests.Inference;

/// <summary>
/// The built-in functions haruspex types: date and string functions, <c>STRING_AGG</c>, <c>JSON_VALUE</c>, window functions,
/// arithmetic and precedence over <c>float</c>, <c>real</c>, <c>money</c> and the date and time types, the bare <c>NULL</c>,
/// and <c>GENERATE_SERIES</c>. Each type and each reason to be null rests on an observation in the differential tests'
/// <c>FunctionObservationTests</c>, named where it matters.
/// </summary>
public partial class FunctionTests
{
    private const string Tables = """
        CREATE TABLE dbo.t (
            id int NOT NULL PRIMARY KEY, g int NOT NULL, n int NOT NULL, v int NULL, ti tinyint NOT NULL, bi bigint NULL,
            s varchar(10) NOT NULL, ns nvarchar(20) NULL, c char(5) NOT NULL, nc nchar(4) NULL, vm varchar(max) NULL, nm nvarchar(max) NULL,
            d decimal(9,2) NOT NULL, nd numeric(5,1) NULL, f float NOT NULL, r real NULL, m money NOT NULL, sm smallmoney NULL,
            da date NOT NULL, dt datetime NOT NULL, sdt smalldatetime NULL, d2 datetime2(3) NOT NULL, d21 datetime2(1) NULL,
            tm time(2) NULL, dto datetimeoffset(4) NULL, doc nvarchar(max) NULL,
            status varchar(10) NOT NULL CONSTRAINT ck_status CHECK (status IN ('open', 'shut')));
        GO
        """;

    private static IrDocument Procedure(string body) =>
        InferProcedure.Infer("dbo.p", ("tables.sql", Tables), ("p.sql", $"CREATE PROCEDURE dbo.p AS\n{body}"));

    // Stricter than IrNavigation.Fields: asserts one result, an array of rows.
    private static EquatableArray<IrField> Fields(IrDocument doc)
    {
        var results = doc.Objects[0].Results ?? throw new InvalidOperationException("The results are unknown: " + string.Join("; ", doc.Unknowns.Select(u => u.Detail)));
        var row = (IrRef)((IrArray)Assert.Single(results).Type).Items;
        return doc.Types[row.Name].Fields;
    }

    private static IrField Field(IrDocument doc, string name) => Assert.Single(Fields(doc), f => f.Name == name);

    /// <summary>The field's SQL type and its reasons to be null, <c>int nullable-column</c>, or "unknown: reason".</summary>
    private static string Typed(IrDocument doc, string name) => Field(doc, name).Type switch
    {
        IrScalar scalar => string.Join(" ", [scalar.Sql ?? "(none)", .. Field(doc, name).NullableBecause.Select(r => r.Rule)]),
        _ => "unknown: " + string.Join("; ", doc.Unknowns.Where(u => u.Path.EndsWith("." + name, StringComparison.Ordinal)).Select(u => u.Reason)),
    };

    private static void Unknown(IrDocument doc, string name) => Assert.StartsWith("unknown", Typed(doc, name), StringComparison.Ordinal);
}
