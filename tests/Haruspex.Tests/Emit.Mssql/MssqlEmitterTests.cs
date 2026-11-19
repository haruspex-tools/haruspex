using System.Text.RegularExpressions;
using Haruspex.Emit.Mssql;
using Haruspex.Emit.Mssql.Planning;
using Haruspex.Emit.Zod;
using Haruspex.Ir;

namespace Haruspex.Tests.Emit.Mssql;

/// <summary>
/// The database module's functions on hand-built IR: what each sends, how, and what it reads. That the functions run against the
/// server is <c>MssqlFunctionTests</c>'; that they compile and lint is the emit check's.
/// </summary>
public partial class MssqlEmitterTests
{
    private static readonly IrFactSource Type = new(IrFactSourceKind.Type, "t");

    private static IrObject Procedure(string name, IrParameter[] parameters, params IrResult[] results) =>
        new("shop." + name, name, IrObjectKind.Procedure, "p.sql:1:1", [.. parameters], [.. results]);

    private static IrDocument Document(params IrObject[] objects) =>
        new(new IrSource("tsql"), [.. objects], new Dictionary<string, IrObjectType>(), []);

    private static IrParameter Parameter(string name, IrType type, bool hasDefault = false, bool output = false) => new(name, type, hasDefault, output);

    private static readonly IrScalar Int = new(IrScalarKind.Number, IrFormat.Int32, "int");

    private static readonly IrResult Rows = new(IrEncoding.Rowset, new IrArray(Int), []);

    private static readonly IrResult Json = new(IrEncoding.Json, Int, []);

    private static string Emitted(params IrObject[] objects) => MssqlEmitter.Emit(Document(objects)).Expected();

    [Theory]
    [InlineData("GetOrder", "getOrder")]
    [InlineData("URLParts", "urlParts")]
    [InlineData("ID", "id")]
    [InlineData("getOrder", "getOrder")]
    [InlineData("get_order", "get_order")]
    [InlineData("A", "a")]
    public void A_function_is_named_for_its_procedure_with_a_lower_case_first_word(string procedure, string function) =>
        Assert.Equal(function, FunctionPlanner.FunctionName(procedure));

    [Fact]
    public void The_parameters_are_checked_before_the_connection_is_touched()
    {
        var ts = Emitted(Procedure("GetOrder", [Parameter("@order_id", Int)], Rows));

        Assert.Matches(
            @"export async function getOrder\(db: Queryable, params: types\.GetOrderParams\): Promise<types\.GetOrder> \{\n" +
            @"  const p = await checked\(GetOrderParamsSchema, params, \(issues\) => new ParameterError\('shop\.GetOrder', issues\)\)\n" +
            @"  const request = db\.request\(\)\n" +
            @"  request\.input\('order_id', sql\.Int, p\.order_id\)\n",
            ts);
    }

    [Fact]
    public void A_procedure_without_parameters_takes_only_the_connection() =>
        Assert.Contains("export async function listOrders(db: Queryable): Promise<", Emitted(Procedure("ListOrders", [], Rows)), StringComparison.Ordinal);

    [Fact]
    public void When_every_parameter_may_be_left_out_so_may_the_parameters_object()
    {
        var ts = Emitted(Procedure("P", [Parameter("@note", Int, hasDefault: true)], Rows), Procedure("Q", [Parameter("@note", Int, hasDefault: true), Parameter("@id", Int)], Rows));

        Assert.Contains("export async function p(db: Queryable, params: types.PParams = {}): ", ts, StringComparison.Ordinal);
        Assert.Contains("export async function q(db: Queryable, params: types.QParams): ", ts, StringComparison.Ordinal);
    }

    [Fact]
    public void A_parameter_with_a_default_is_bound_only_when_given()
    {
        // Bound as undefined, the driver sends NULL, which overrides the default.
        var ts = Emitted(Procedure("P", [Parameter("@note", Int, hasDefault: true)], Rows));

        Assert.Contains("  if (p.note !== undefined) request.input('note', sql.Int, p.note)\n", ts, StringComparison.Ordinal);
    }

    [Fact]
    public void An_output_parameter_is_bound_as_output_always_without_a_default_and_when_given_with_one()
    {
        var ts = Emitted(Procedure("P", [Parameter("@total", Int, output: true), Parameter("@count", Int, hasDefault: true, output: true)], Rows));

        Assert.Contains("  request.output('total', sql.Int, p.total)\n", ts, StringComparison.Ordinal);
        Assert.Contains("  if (p.count !== undefined) request.output('count', sql.Int, p.count)\n", ts, StringComparison.Ordinal);
        Assert.Contains("  const output = await checked(POutputSchema, response.output, (issues) => new ResultError('shop.P', 'the OUTPUT parameters', issues))\n", ts, StringComparison.Ordinal);
        Assert.Contains("Promise<{ result: types.P; output: types.POutput }>", ts, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("sql.NVarChar(40)", IrScalarKind.String, null, 40, IrLengthUnit.Utf16, false)]
    [InlineData("sql.NChar(3)", IrScalarKind.String, null, 3, IrLengthUnit.Utf16, true)]
    [InlineData("sql.VarChar(20)", IrScalarKind.String, null, 20, IrLengthUnit.Bytes, false)]
    [InlineData("sql.Char(8)", IrScalarKind.String, null, 8, IrLengthUnit.Bytes, true)]
    [InlineData("sql.NVarChar(sql.MAX)", IrScalarKind.String, null, 0, IrLengthUnit.Utf16, false)]
    [InlineData("sql.TinyInt", IrScalarKind.Number, IrFormat.Uint8, 0, IrLengthUnit.Utf16, false)]
    [InlineData("sql.SmallInt", IrScalarKind.Number, IrFormat.Int16, 0, IrLengthUnit.Utf16, false)]
    [InlineData("sql.BigInt", IrScalarKind.Number, IrFormat.Int64, 0, IrLengthUnit.Utf16, false)]
    [InlineData("sql.Float", IrScalarKind.Number, IrFormat.Float, 0, IrLengthUnit.Utf16, false)]
    [InlineData("sql.Bit", IrScalarKind.Boolean, null, 0, IrLengthUnit.Utf16, false)]
    [InlineData("sql.UniqueIdentifier", IrScalarKind.Uuid, null, 0, IrLengthUnit.Utf16, false)]
    // A date or time is sent as its ISO 8601 text, which the server converts exactly (decision 0055).
    [InlineData("sql.VarChar(sql.MAX)", IrScalarKind.Date, null, 0, IrLengthUnit.Utf16, false)]
    [InlineData("sql.VarChar(sql.MAX)", IrScalarKind.Datetime, IrFormat.Local, 0, IrLengthUnit.Utf16, false)]
    // A binary value is sent as its declared type, of which the driver pads a shorter Buffer to a fixed length.
    [InlineData("sql.VarBinary(16)", IrScalarKind.Bytes, null, 16, IrLengthUnit.Bytes, false)]
    [InlineData("sql.Binary(32)", IrScalarKind.Bytes, null, 32, IrLengthUnit.Bytes, true)]
    [InlineData("sql.VarBinary(sql.MAX)", IrScalarKind.Bytes, null, 0, IrLengthUnit.Bytes, false)]
    public void Each_parameter_is_sent_with_the_driver_s_type_for_its_declared_one(string driver, IrScalarKind kind, IrFormat? format, int length, IrLengthUnit unit, bool isFixed)
    {
        var facets = length > 0 ? new IrFacets(Length: new IrLength(length, isFixed, unit, IrByteEncoding.Unknown, Type)) : null;
        var ts = Emitted(Procedure("P", [Parameter("@v", new IrScalar(kind, format, "t", facets))], Rows));

        Assert.Contains($"  request.input('v', {driver}, p.v)\n", ts, StringComparison.Ordinal);
    }

    [Fact]
    public void A_decimal_is_sent_with_its_precision_and_scale()
    {
        var money = new IrScalar(IrScalarKind.Number, IrFormat.Decimal, "money", new IrFacets(Digits: new IrDigits(19, 4, Type)));

        Assert.Contains("request.input('v', sql.Decimal(19, 4), p.v)", Emitted(Procedure("P", [Parameter("@v", money)], Rows)), StringComparison.Ordinal);
    }

    [Fact]
    public void A_parameter_whose_type_is_not_known_is_sent_for_the_driver_to_type() =>
        Assert.Contains(
            "  if (p.lines !== undefined) request.input('lines', p.lines)\n",
            Emitted(Procedure("P", [Parameter("@lines", new IrUnknownType(IrUnknownReason.UnsupportedSqlType), hasDefault: true)], Rows)),
            StringComparison.Ordinal);

    [Fact]
    public void Each_result_set_is_read_by_its_encoding_and_several_are_named_in_order()
    {
        var ts = Emitted(Procedure("P", [], Rows, Json));

        Assert.Contains("Promise<{ result1: types.P[0]; result2: types.P[1] }>", ts, StringComparison.Ordinal);
        Assert.Contains("  const [result1, result2] = await checked(PSchema, [readRows(sets[0]), readJson(sets[1])], ", ts, StringComparison.Ordinal);
        Assert.Contains("  return { result1, result2 }\n", ts, StringComparison.Ordinal);
    }

    [Fact]
    public void One_result_set_with_no_output_and_no_return_code_is_returned_itself()
    {
        var ts = Emitted(Procedure("P", [], Rows));

        Assert.Contains("export async function p(db: Queryable): Promise<types.P> {\n", ts, StringComparison.Ordinal);
        Assert.Contains("  return result\n}\n", ts, StringComparison.Ordinal);
    }

    [Fact]
    public void A_return_code_other_than_0_comes_back_named_beside_the_result()
    {
        var ts = Emitted(Procedure("P", [], Rows) with { ReturnsCode = true }, Procedure("Q", []) with { ReturnsCode = true });

        Assert.Contains("export async function p(db: Queryable): Promise<{ result: types.P; returnValue: number }> {\n", ts, StringComparison.Ordinal);
        Assert.Contains("  return { result, returnValue: response.returnValue }\n", ts, StringComparison.Ordinal);
        Assert.Contains("export async function q(db: Queryable): Promise<{ returnValue: number }> {\n", ts, StringComparison.Ordinal);
    }

    [Fact]
    public void A_procedure_of_no_result_sets_no_output_and_no_return_code_returns_nothing()
    {
        var ts = Emitted(Procedure("P", []));

        Assert.Contains(
            "export async function p(db: Queryable): Promise<void> {\n  const request = db.request()\n  await request.execute('shop.P')\n}\n",
            ts,
            StringComparison.Ordinal);
        Assert.DoesNotContain("PSchema", ts, StringComparison.Ordinal);
    }

    private static readonly IrResult NotRun = new(IrEncoding.Rowset, new IrArray(Int), [new IrNullReason(IrNullRule.NotRun, "p.sql:2:1")]);

    private static string Strict(params IrObject[] objects) => MssqlEmitter.Emit(Document(objects), new MssqlOptions { RefusesExtraResultSets = true }).Expected();

    [Fact]
    public void Fewer_result_sets_than_every_call_returns_fail_and_more_are_ignored_by_default()
    {
        // Two sets every call returns, then one that may not run: fewer than two is a database out of step; a third, or a
        // fourth the function doesn't know, is read past (decision 0063).
        var ts = Emitted(Procedure("P", [], Rows, Rows, NotRun));

        Assert.Contains(
            "  if (sets.length < 2) throw new ResultError('shop.P', 'the results', [{ message: `Expected at least 2 result sets, received ${String(sets.length)}` }])\n",
            ts,
            StringComparison.Ordinal);
        Assert.DoesNotContain("sets.length >", ts, StringComparison.Ordinal);
    }

    [Fact]
    public void A_for_json_set_absent_only_when_no_rows_match_still_counts_as_arriving()
    {
        // FOR JSON over no rows still sends its set, one row of empty text (observed); only a statement that doesn't run leaves
        // a set out. So both sets below always arrive.
        var noRows = new IrResult(IrEncoding.Json, Int, [new IrNullReason(IrNullRule.NoRows, "p.sql:2:1")]);

        Assert.Contains("  if (sets.length < 2) throw ", Emitted(Procedure("P", [], noRows, Rows)), StringComparison.Ordinal);
        Assert.Contains("  if (sets.length !== 2) throw ", Strict(Procedure("P", [], noRows, Rows)), StringComparison.Ordinal);
    }

    [Fact]
    public void A_function_whose_sets_may_all_be_missing_checks_no_count_by_default()
    {
        Assert.DoesNotContain("sets.length", Emitted(Procedure("P", [], NotRun)), StringComparison.Ordinal);
        Assert.DoesNotContain("sets.length", Emitted(Procedure("P", [])), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("rows, rows", "sets.length !== 2", "Expected exactly 2 result sets")]
    [InlineData("rows, not run", "sets.length < 1 || sets.length > 2", "Expected between 1 and 2 result sets")]
    [InlineData("not run", "sets.length > 1", "Expected at most one result set")]
    [InlineData("rows", "sets.length !== 1", "Expected exactly one result set")]
    [InlineData("", "sets.length > 0", "Expected no result sets")]
    public void Under_strict_objects_a_result_set_the_function_does_not_know_fails(string results, string condition, string expected)
    {
        IrResult[] sets = [.. results.Split(", ", StringSplitOptions.RemoveEmptyEntries).Select(r => r == "rows" ? Rows : NotRun)];

        var ts = Strict(Procedure("P", [], sets));

        Assert.Contains(
            $"  if ({condition}) throw new ResultError('shop.P', 'the results', [{{ message: `{expected}, received ${{String(sets.length)}}` }}])\n",
            ts,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Under_strict_objects_a_procedure_of_no_result_sets_still_reads_the_response()
    {
        Assert.Contains(
            "  const response = await request.execute('shop.P')\n  const sets = response.recordsets as unknown as readonly unknown[]\n  if (sets.length > 0) ",
            Strict(Procedure("P", [])),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Results_that_cannot_be_listed_are_handed_over_as_they_arrive_through_their_schema()
    {
        var unknown = new IrObject("shop.p", "P", IrObjectKind.Procedure, "p.sql:1:1", [], null);

        Assert.Contains("  const result = await checked(PSchema, sets, ", Emitted(unknown), StringComparison.Ordinal);
    }

    [Fact]
    public void A_helper_is_written_only_where_a_function_uses_it()
    {
        // A linter reports a function nothing calls.
        var ts = Emitted(Procedure("P", []));

        Assert.DoesNotContain("function readRows", ts, StringComparison.Ordinal);
        Assert.DoesNotContain("function readJson", ts, StringComparison.Ordinal);
        Assert.DoesNotContain("function checked", ts, StringComparison.Ordinal);
    }

    [Fact]
    public void The_database_s_own_error_is_not_caught() =>
        Assert.DoesNotContain("catch", Emitted(Procedure("P", [Parameter("@v", Int)], Rows, Json)), StringComparison.Ordinal);

    [Fact]
    public void The_module_imports_its_schemas_by_name_and_its_types_through_a_namespace()
    {
        var ts = MssqlEmitter.Emit(
            Document(Procedure("B", [Parameter("@v", Int)], Rows), Procedure("A", [], Rows)),
            new MssqlOptions { SchemaModule = "./db.zod" }).Expected();

        Assert.Contains("import type * as types from './db.zod'\nimport {\n  ASchema,\n  BParamsSchema,\n  BSchema,\n} from './db.zod'\n", ts, StringComparison.Ordinal);
    }

    [Fact]
    public void The_schemas_are_imported_by_the_file_they_compile_to_by_default() =>

        // An ECMAScript module needs the .js, which TypeScript maps to the .ts under every module resolution.
        Assert.Contains("} from './haruspex.zod.js'\n", Emitted(Procedure("P", [], Rows)), StringComparison.Ordinal);

    [Fact]
    public void Server_only_is_imported_first_when_asked_and_never_otherwise()
    {
        var document = Document(Procedure("P", [], Rows));

        Assert.Contains("\nimport 'server-only'\nimport sql from 'mssql'\n", MssqlEmitter.Emit(document, new MssqlOptions { ServerOnly = true }).Expected(), StringComparison.Ordinal);
        Assert.DoesNotContain("server-only", MssqlEmitter.Emit(document).Expected(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Delete", "reserved")]
    [InlineData("Sql", "uses for itself")]
    [InlineData("readJson", "uses for itself")]
    public void A_function_name_that_is_reserved_or_the_module_s_own_is_refused(string procedure, string why)
    {
        var refusal = Assert.Single(MssqlEmitter.Emit(Document(Procedure(procedure, [], Rows)), new MssqlOptions { IsReserved = TypeScriptNames.IsReserved }).Diagnostics);

        Assert.Equal(MssqlCodes.FunctionNameTaken, refusal.Code);
        Assert.Contains(why, refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_function_named_as_its_own_results_type_is_not_refused() =>

        // Camel-case naming names the type getOrder too; the types come through their namespace.
        Assert.Contains("export async function getOrder(db: Queryable): Promise<types.getOrder> {", Emitted(Procedure("getOrder", [], Rows)), StringComparison.Ordinal);

    [Fact]
    public void Two_procedures_whose_functions_would_share_a_name_are_refused()
    {
        var refusal = Assert.Single(MssqlEmitter.Emit(Document(Procedure("GetX", [], Rows), Procedure("getX", [], Rows))).Diagnostics);

        Assert.Contains("the function of shop.GetX too", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_name_the_module_binds_for_itself_is_one_the_planner_keeps_from_functions()
    {
        // A procedure that needs every helper, a table-valued parameter's among them, whose function is left out of the names
        // declared; sql and types are imported.
        var ts = MssqlEmitter.Emit(WithTableValued(Document(Procedure("P", [Parameter("@v", Int)], Rows, Json)))).Expected();
        var declared = Declarations().Matches(ts).Select(m => m.Groups["name"].Value).Where(n => n is not "p" and not "addLines").Append("sql").Append("types").Order(StringComparer.Ordinal);

        Assert.Equal(FunctionPlanner.ModuleNames.Order(StringComparer.Ordinal), declared);
    }

    [Fact]
    public void Every_name_the_functions_import_is_one_the_zod_module_exports()
    {
        // The two emitters meet only in the generated files (SchemaModuleNames); this holds the names together.
        var document = Document(
            Procedure("GetOrder", [Parameter("@order_id", Int), Parameter("@total", Int, output: true)], Rows),
            Procedure("ListOrders", [], Rows, Json));
        var functions = MssqlEmitter.Emit(document).Expected();
        var schemas = ZodEmitter.Emit(document).Expected();

        var imported = Imported().Match(functions).Groups["names"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Concat(TypesUsed().Matches(functions).Select(m => m.Groups["name"].Value))
            .ToList();
        Assert.NotEmpty(imported);
        Assert.All(imported, name => Assert.Matches($@"\nexport (const|type) {name} = ", schemas));
    }

    [GeneratedRegex(@"^(?:export )?(?:interface|class|async function|function|const) (?<name>\w+)", RegexOptions.Multiline)]
    private static partial Regex Declarations();

    [GeneratedRegex(@"\nimport \{(?<names>[^}]*)\} from")]
    private static partial Regex Imported();

    [GeneratedRegex(@"\btypes\.(?<name>\w+)")]
    private static partial Regex TypesUsed();
}
