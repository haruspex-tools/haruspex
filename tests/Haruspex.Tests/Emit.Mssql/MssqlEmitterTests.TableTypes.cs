using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.Emit.Mssql;
using Haruspex.Ir;

namespace Haruspex.Tests.Emit.Mssql;

/// <summary>A table-valued parameter's binding: an <c>sql.Table</c> of its type, every column declared, in the type's order.</summary>
public partial class MssqlEmitterTests
{
    private static readonly IrScalar Remark = new(IrScalarKind.String, null, "nvarchar(20)", new IrFacets(Length: new IrLength(20, false, IrLengthUnit.Utf16, IrByteEncoding.Unknown, Type)));

    private static readonly IrScalar Day = new(IrScalarKind.Date, null, "date");

    /// <summary>The document with a procedure <c>shop.AddLines</c> taking <c>@lines</c> of <c>[shop].[line list]</c>, whose columns are as given.</summary>
    private static IrDocument WithTableValued(IrDocument document, params (string Name, IrType Type)[] columns)
    {
        if (columns.Length == 0)
            columns = [("first col", Int), ("select", Remark), ("day", Day)];
        var row = new IrObjectType([.. columns.Select(c => new IrField(c.Name, c.Type, IrPresence.Always, []))])
        {
            Identity = new IrTypeIdentity(IrTypeOwner.TableType, "shop.line list", "<shop.line list>"),
        };
        var definition = new IrTableType("shop", "line list", [.. columns.Select(c => new IrTableColumn(c.Name, (c.Type as IrScalar)?.Sql, false) { Type = c.Type as IrScalar })], "t.sql:1:1");
        var addLines = Procedure("AddLines", [new IrParameter("@lines", new IrArray(new IrRef("LineListRow")), HasDefault: true, IsOutput: false) { TableType = "shop.line list" }], Rows);
        return document with
        {
            Objects = [.. document.Objects, addLines],
            Types = new Dictionary<string, IrObjectType>(document.Types) { ["LineListRow"] = row },
            TableTypes = new Dictionary<string, IrTableType> { ["shop.line list"] = definition }.ToEquatableMap(t => t.Key, t => t.Value),
        };
    }

    [Fact]
    public void A_table_valued_parameter_is_sent_as_a_table_of_its_type_by_schema_and_name_every_column_in_the_type_s_order()
    {
        var ts = MssqlEmitter.Emit(WithTableValued(Document())).Expected();

        // Named apart, so a name with a space or a dot needs no quoting; a date as text, as a scalar parameter is sent.
        Assert.Contains(
            "  if (p.lines !== undefined) request.input('lines', table('shop', 'line list', [['first col', sql.Int], ['select', sql.NVarChar(20)], ['day', sql.VarChar(sql.MAX)]], p.lines))\n",
            ts,
            StringComparison.Ordinal);
        Assert.Contains("function table(", ts, StringComparison.Ordinal);
        Assert.Contains("function cell(", ts, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("date", IrScalarKind.Date, null)]
    [InlineData("time(3)", IrScalarKind.Time, null)]
    [InlineData("datetime2(0)", IrScalarKind.Datetime, IrFormat.Local)]
    [InlineData("datetime", IrScalarKind.Datetime, IrFormat.Local)]
    [InlineData("smalldatetime", IrScalarKind.Datetime, IrFormat.Local)]
    [InlineData("datetimeoffset(7)", IrScalarKind.Datetime, IrFormat.Offset)]
    public void A_table_type_s_date_or_time_column_is_declared_to_the_driver_as_text(string sql, IrScalarKind kind, IrFormat? format)
    {
        // As the driver's own date types, a value would be read in the process's time zone (decision 0055).
        var ts = MssqlEmitter.Emit(WithTableValued(Document(), ("at", new IrScalar(kind, format, sql)))).Expected();

        Assert.Contains("[['at', sql.VarChar(sql.MAX)]]", ts, StringComparison.Ordinal);
    }

    [Fact]
    public void A_module_that_sends_no_table_has_no_helper_to_build_one() =>

        // A linter reports a function nothing calls.
        Assert.DoesNotContain("function table(", Emitted(Procedure("P", [Parameter("@v", Int)], Rows)), StringComparison.Ordinal);

    [Fact]
    public void A_table_type_s_column_the_driver_can_t_be_told_the_type_of_is_refused()
    {
        var document = WithTableValued(Document(), ("qty", new IrScalar(IrScalarKind.Number, null, "numeric")));

        var refusal = Assert.Single(Assert.IsType<Failed<string>>(MssqlEmitter.Emit(document)).Diagnostics);

        Assert.Equal(MssqlCodes.TableColumnCannotBeDeclared, refusal.Code);
        Assert.Equal("The function of shop.AddLines must declare the column qty of the table type shop.line list to the driver, and its type gives no driver type to declare it as.", refusal.Message);
    }
}
