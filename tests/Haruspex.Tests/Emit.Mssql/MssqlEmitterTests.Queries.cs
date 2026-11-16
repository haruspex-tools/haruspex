using Haruspex.Emit.Mssql;
using Haruspex.Emit.Zod;
using Haruspex.Ir;

namespace Haruspex.Tests.Emit.Mssql;

/// <summary>
/// A query file's function: its text sent through <c>sp_executesql</c>, each parameter declared by its type's SQL and bound by
/// the driver's type, with the procedures' checks and reads. That it runs on the server is <c>MssqlFunctionTests.Queries.cs</c>'.
/// </summary>
public partial class MssqlEmitterTests
{
    private const string Text = "SELECT o.order_id FROM shop.orders AS o\nWHERE o.customer_id = @customer_id AND o.note = N'it''s';\n";

    private static IrObject Query(string name, IrParameter[] parameters, string text = Text, params IrResult[] results) =>
        new("sql/" + name + ".sql", name, IrObjectKind.Query, "sql/" + name + ".sql:1:1", [.. parameters], [.. results]) { Text = text };

    private static readonly IrScalar Note = new(IrScalarKind.String, null, "nvarchar(40)", new IrFacets(Length: new IrLength(40, IsFixed: false, IrLengthUnit.Utf16, IrByteEncoding.Unknown, Type)));

    [Fact]
    public void A_query_file_s_function_sends_its_text_through_sp_executesql_declaring_each_parameter_by_its_type()
    {
        var ts = Emitted(Query("OrdersOf", [Parameter("@customer_id", Int), Parameter("@note", Note)], Text, Rows));

        Assert.Contains(
            "/** Runs the query file sql/OrdersOf.sql. */\n" +
            "export async function ordersOf(db: Queryable, params: types.OrdersOfParams): Promise<types.OrdersOf> {\n" +
            "  const p = await checked(OrdersOfParamsSchema, params, (issues) => new ParameterError('sql/OrdersOf.sql', issues))\n" +
            "  const request = db.request()\n" +
            "  request.input('stmt', sql.NVarChar(sql.MAX), 'SELECT o.order_id FROM shop.orders AS o\\nWHERE o.customer_id = @customer_id AND o.note = N\\'it\\'\\'s\\';\\n')\n" +
            "  request.input('params', sql.NVarChar(sql.MAX), '@customer_id int, @note nvarchar(40)')\n" +
            "  request.input('customer_id', sql.Int, p.customer_id)\n" +
            "  request.input('note', sql.NVarChar(40), p.note)\n" +
            "  const response = await request.execute('sys.sp_executesql')\n",
            ts,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_query_file_without_parameters_sends_its_text_alone() =>
        Assert.Contains(
            "export async function listOrders(db: Queryable): Promise<types.ListOrders> {\n" +
            "  const request = db.request()\n" +
            "  request.input('stmt', sql.NVarChar(sql.MAX), 'SELECT 1 AS one;')\n" +
            "  const response = await request.execute('sys.sp_executesql')\n",
            Emitted(Query("ListOrders", [], "SELECT 1 AS one;", Rows)),
            StringComparison.Ordinal);

    [Fact]
    public void A_query_parameter_of_no_known_type_is_refused_since_the_call_must_declare_one()
    {
        var refusal = Assert.Single(MssqlEmitter.Emit(Document(Query("Label", [Parameter("@label", new IrUnknownType(IrUnknownReason.UndecidedParameter))], "SELECT @label AS label;", Rows))).Diagnostics);

        Assert.Equal(MssqlCodes.QueryParameterCannotBeDeclared, refusal.Code);
        Assert.Equal(
            "The function of sql/Label.sql must declare @label to sp_executesql, and its type isn't known (undecided-parameter); declare it in the file, as -- @param @label <type>.",
            refusal.Message);
    }

    [Theory]
    [InlineData("@stmt")]
    [InlineData("@PARAMS")]
    public void A_query_parameter_named_as_one_of_sp_executesql_s_own_is_refused(string name)
    {
        var refusal = Assert.Single(MssqlEmitter.Emit(Document(Query("Q", [Parameter(name, Int)], $"SELECT {name} AS v;", Rows))).Diagnostics);

        Assert.Equal(MssqlCodes.QueryParameterCannotBeDeclared, refusal.Code);
        Assert.Equal(
            $"The function of sql/Q.sql must declare {name} to sp_executesql, which has a parameter of that name itself; rename it in the file.",
            refusal.Message);
    }

    [Theory]
    [InlineData("sql", "is a name the module uses for itself")]
    [InlineData("delete", "is a reserved word")]
    public void A_query_file_whose_function_can_not_take_its_name_is_told_to_rename_the_file(string name, string why)
    {
        var refusal = Assert.Single(MssqlEmitter.Emit(Document(Query(name, [], "SELECT 1 AS one;", Rows)), new MssqlOptions { IsReserved = TypeScriptNames.IsReserved }).Diagnostics);

        Assert.Equal($"The function of sql/{name}.sql would be named {name}, which {why}; rename the query file.", refusal.Message);
    }

    [Fact]
    public void Two_query_files_whose_functions_would_share_a_name_are_told_to_rename_a_file() =>
        Assert.Equal(
            "The function of sql/getX.sql would be named getX, which is the function of sql/GetX.sql too; rename the query file.",
            Assert.Single(MssqlEmitter.Emit(Document(Query("GetX", [], "SELECT 1 AS one;", Rows), Query("getX", [], "SELECT 1 AS one;", Rows))).Diagnostics).Message);

    [Fact]
    public void A_query_file_and_a_procedure_whose_functions_would_share_a_name_are_refused_naming_both()
    {
        var refusal = Assert.Single(MssqlEmitter.Emit(Document(Procedure("GetOrder", [], Rows), Query("getOrder", [], "SELECT 1 AS one;", Rows))).Diagnostics);

        Assert.Equal(
            "The function of sql/getOrder.sql would be named getOrder, which is the function of shop.GetOrder too; rename the procedure in the config file, or the query file.",
            refusal.Message);
    }
}
