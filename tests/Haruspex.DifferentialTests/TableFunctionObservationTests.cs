using Haruspex.DifferentialTests.Server;

namespace Haruspex.DifferentialTests;

/// <summary>
/// What SQL Server does with table-valued functions read as tables, observed and kept as tests: the rules haruspex's reading of them rests on
/// (Haruspex.SqlServer.Inference's README.md, "Table-valued functions"). What a call's arguments become, what a multi-statement
/// function's return table enforces, and what a call may and may not say. Each test creates what it needs in a write session,
/// rolled back. Observed on SQL Server 2025 RTM-CU10.
/// </summary>
public class TableFunctionObservationTests(SqlServerFixture server)
{
    private Task<CallOutcome> Run(string[] setup, string call, bool errorsAsMessages = false) =>
        ObservedCall.Run(server, setup, call, errorsAsMessages);

    /// <summary>A multi-statement function whose return table declares a key, a unique column, NOT NULL and a CHECK; @mode picks the rows.</summary>
    private const string Declared = """
        CREATE FUNCTION dbo.obs_declared (@mode int) RETURNS @r TABLE (id int NOT NULL PRIMARY KEY, code varchar(3) NULL UNIQUE, n int NULL CHECK (n > 0), label varchar(5) NULL) AS
        BEGIN
            IF @mode = 0 INSERT @r VALUES (1, 'a', 1, 'x'), (2, NULL, NULL, NULL);
            IF @mode = 1 INSERT @r VALUES (1, 'a', 1, 'x'), (1, 'b', 1, 'x');
            IF @mode = 2 INSERT @r VALUES (NULL, 'a', 1, 'x');
            IF @mode = 3 INSERT @r VALUES (1, 'a', 1, 'x'), (2, 'a', 1, 'x');
            IF @mode = 4 INSERT @r VALUES (1, NULL, 1, 'x'), (2, NULL, 1, 'x');
            IF @mode = 5 INSERT @r VALUES (1, 'a', 0, 'x');
            IF @mode = 6 INSERT @r VALUES (1, 'a', 1, 'toolong');
            RETURN;
        END
        """;

    [Fact]
    public async Task A_multi_statement_functions_return_table_enforces_its_constraints()
    {
        // Its return table is a table variable that enforces what it declares: a repeated key or unique value (one NULL at
        // most), a NULL in a NOT NULL column and a value its CHECK refuses each fail the call. A string too long fails too, by
        // default (2628); under a caller's ANSI_WARNINGS off it is cut to the column's length, which is still its type.
        Assert.Equal("id,code,n,label: 1,a,1,x;2,null,null,null", (await Run([Declared], "SELECT * FROM dbo.obs_declared(0) ORDER BY id;")).ToString());
        Assert.Equal(2627, (await Run([Declared], "SELECT * FROM dbo.obs_declared(1);")).Error);
        Assert.Equal(515, (await Run([Declared], "SELECT * FROM dbo.obs_declared(2);")).Error);
        Assert.Equal(2627, (await Run([Declared], "SELECT * FROM dbo.obs_declared(3);")).Error);
        Assert.Equal(2627, (await Run([Declared], "SELECT * FROM dbo.obs_declared(4);")).Error);
        Assert.Equal(547, (await Run([Declared], "SELECT * FROM dbo.obs_declared(5);")).Error);
        Assert.Equal(2628, (await Run([Declared], "SELECT * FROM dbo.obs_declared(6);")).Error);
        Assert.Equal("id,code,n,label: 1,a,1,toolo", (await Run([Declared], "SET ANSI_WARNINGS OFF; SELECT * FROM dbo.obs_declared(6);")).ToString());
    }

    private const string Echo = "CREATE FUNCTION dbo.obs_echo (@p tinyint, @s varchar(3)) RETURNS TABLE AS RETURN SELECT @p AS p, @s AS s;";

    [Fact]
    public async Task An_argument_is_converted_to_its_parameters_type_as_an_assignment_is()
    {
        // A string is cut to the parameter's length; a number that overflows the parameter's type fails the call by default
        // (220), and is NULL under a caller's ANSI_WARNINGS and ARITHABORT off. A NULL passes as NULL.
        Assert.Equal("p,s: 5,abc", (await Run([Echo], "SELECT * FROM dbo.obs_echo(5, 'abcdef');")).ToString());
        Assert.Equal(220, (await Run([Echo], "SELECT * FROM dbo.obs_echo(1000, 'x');")).Error);
        Assert.Equal("p,s: null,x", (await Run([Echo], "SET ANSI_WARNINGS OFF; SET ARITHABORT OFF; SELECT * FROM dbo.obs_echo(1000, 'x');")).ToString());
        Assert.Equal("p,s: null,null", (await Run([Echo], "SELECT * FROM dbo.obs_echo(NULL, NULL);")).ToString());
    }

    [Fact]
    public async Task Default_passes_the_parameters_default()
    {
        Assert.Equal(
            "x: 7",
            (await Run(["CREATE FUNCTION dbo.obs_default (@x int = 7) RETURNS TABLE AS RETURN SELECT @x AS x;"], "SELECT * FROM dbo.obs_default(DEFAULT);")).ToString());
    }

    [Fact]
    public async Task A_call_takes_no_column_alias_and_no_table_hint()
    {
        Assert.Equal(317, (await Run([Echo], "SELECT * FROM dbo.obs_echo(1, 'a') AS x (a, b);")).Error);
        Assert.Equal(102, (await Run([Echo], "SELECT * FROM dbo.obs_echo(1, 'a') WITH (NOLOCK);")).Error);
    }

    [Fact]
    public async Task An_aggregate_function_applied_gives_one_row_for_every_outer_row()
    {
        // As an applied aggregate query does (ApplyViewObservationTests): through an inline function too, an outer row without
        // rows still gets one, so neither APPLY drops or null-extends it.
        string[] setup = [
            """
            CREATE TABLE dbo.obs_outer (id int NOT NULL PRIMARY KEY);
            CREATE TABLE dbo.obs_line (id int NOT NULL PRIMARY KEY, outer_id int NOT NULL, amount decimal(10,2) NOT NULL);
            INSERT INTO dbo.obs_outer VALUES (1), (2);
            INSERT INTO dbo.obs_line VALUES (1, 1, 2.50), (2, 1, 1.25);
            """,
            "CREATE FUNCTION dbo.obs_totals (@id int) RETURNS TABLE AS RETURN SELECT COUNT_BIG(*) AS lines, COALESCE(SUM(l.amount), 0) AS total FROM dbo.obs_line AS l WHERE l.outer_id = @id;",
        ];
        Assert.Equal("id,lines,total: 1,2,3.75;2,0,0.00", (await Run(setup, "SELECT o.id, t.lines, t.total FROM dbo.obs_outer AS o CROSS APPLY dbo.obs_totals(o.id) AS t ORDER BY o.id;")).ToString());
        Assert.Equal("id,lines,total: 1,2,3.75;2,0,0.00", (await Run(setup, "SELECT o.id, t.lines, t.total FROM dbo.obs_outer AS o OUTER APPLY dbo.obs_totals(o.id) AS t ORDER BY o.id;")).ToString());
    }
}
