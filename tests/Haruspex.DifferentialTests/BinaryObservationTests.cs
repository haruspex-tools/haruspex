using System.Globalization;
using Haruspex.DifferentialTests.Server;

namespace Haruspex.DifferentialTests;

/// <summary>
/// What SQL Server gives for binary values (decision 0081): the types the expressions that make them take, how long their values
/// are, when they are NULL, and how <c>FOR JSON</c> writes them. Types are read as the server describes them
/// (<c>sys.dm_exec_describe_first_result_set</c>), values as hex text (<c>CONVERT(varchar(max), x, 1)</c>) or their
/// <c>DATALENGTH</c>. The rules rest on these (Haruspex.SqlServer.Inference's README.md, "Binary values"). Observed on SQL Server
/// 2025 RTM-CU10. What the mssql driver hands over for them is in <c>MssqlDriverObservationTests</c>.
/// </summary>
public class BinaryObservationTests(SqlServerFixture server)
{
    private Task<CallOutcome> Run(string call, params string[] setup) => ObservedCall.Run(server, setup, call);

    /// <summary>The server's description of a query's columns: <c>name type</c>, comma-separated.</summary>
    private Task<string> Describe(string select) => FirstResultSet.Describe(server, select);

    /// <summary>A row of a value of each binary type, each given, so that a column's type is its own.</summary>
    private const string Row = """
        (SELECT CAST(0x01 AS varbinary(2)) AS v2, CAST(0x01 AS binary(10)) AS b10, CAST(0x01 AS binary(2)) AS b2,
                CAST(0x01 AS binary(12)) AS b12, CAST(0x01 AS varbinary(10)) AS v10, CAST(0x01 AS varbinary(max)) AS vm,
                CAST(0x01 AS binary(5000)) AS b5k, CAST(0x01 AS varbinary(5000)) AS v5k, CAST(0x0000000000000001 AS rowversion) AS rv,
                CAST('a' AS varchar(3)) AS a, CAST(N'a' AS nvarchar(3)) AS nb, CAST(1 AS int) AS i) AS x
        """;

    // ---- conversions ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_binary_type_without_a_length_is_30_bytes_long_in_a_conversion() =>
        Assert.Equal(
            "c1 varbinary(30), c2 binary(30), c3 varbinary(30), c4 varbinary(4), c5 varbinary(max), c6 varchar(10), c7 varchar(10)",
            await Describe("SELECT CAST(1 AS varbinary) AS c1, CAST(1 AS binary) AS c2, CONVERT(varbinary, 'ab') AS c3, CONVERT(varbinary(4), '0x0A', 1) AS c4, CONVERT(varbinary(max), '0A', 2) AS c5, CONVERT(varchar(10), 0x0A, 1) AS c6, CONVERT(varchar(10), 0x0A, 2) AS c7"));

    [Fact]
    public async Task A_conversion_into_rowversion_makes_a_timestamp_of_8_bytes_and_floating_point_a_guid_or_a_date_is_refused()
    {
        // rowversion and timestamp name one type, a value of which is its bytes cut or padded to 8 (the edges, with the options
        // off, are in ConversionObservationTests). Into it a float, a real, a GUID, a date, a datetime2, a time or a
        // datetimeoffset is refused (529).
        Assert.Equal(
            "c1 timestamp, c2 timestamp, c3 timestamp, c4 timestamp",
            await Describe("SELECT CAST(b2 AS rowversion) AS c1, CAST(i AS timestamp) AS c2, CONVERT(rowversion, a) AS c3, CAST(nb AS rowversion) AS c4 FROM " + Row));
        Assert.Equal(
            "v: 0x0102000000000000",
            (await Run("SET ANSI_WARNINGS OFF; SET ARITHABORT OFF; SELECT CONVERT(varchar(20), CONVERT(binary(8), CAST(0x0102 AS rowversion)), 1) AS v;")).ToString());
        foreach (var refused in new[] { "CAST(1e0 AS real)", "1e0", "NEWID()", "CAST(GETDATE() AS date)", "SYSDATETIME()", "CAST(GETDATE() AS time)", "SYSDATETIMEOFFSET()" })
            Assert.Equal(529, (await Run($"SELECT CAST({refused} AS rowversion) AS v;")).Error);
    }

    [Fact]
    public async Task Converting_between_binary_types_cuts_or_pads_without_an_error_or_null_under_any_options()
    {
        // A longer value is cut short and a shorter one padded with zeros to a fixed length, under the default options and under
        // the caller's ANSI_WARNINGS and ARITHABORT off alike: never an error, never NULL.
        const string Converted = "SELECT CONVERT(varchar(20), CAST(0x010203 AS binary(2)), 1) AS t1, CONVERT(varchar(20), CAST(0x010203 AS varbinary(2)), 1) AS t2, CONVERT(varchar(20), CAST(0x01 AS binary(4)), 1) AS pad;";
        Assert.Equal("t1,t2,pad: 0x0102,0x0102,0x01000000", (await Run(Converted)).ToString());
        Assert.Equal("t1,t2,pad: 0x0102,0x0102,0x01000000", (await Run("SET ANSI_WARNINGS OFF; SET ARITHABORT OFF; " + Converted)).ToString());
    }

    [Fact]
    public async Task Text_read_as_hex_into_binary_is_an_error_when_it_is_not_hex_and_null_under_try_convert()
    {
        // Under the caller's ANSI_WARNINGS and ARITHABORT off as well: an error, never NULL, but for TRY_CONVERT.
        Assert.Equal(" ! 8114", (await Run("SELECT CONVERT(varbinary(4), '0xZZ', 1) AS b;")).ToString());
        Assert.Equal(" ! 8114", (await Run("SET ANSI_WARNINGS OFF; SET ARITHABORT OFF; SELECT CONVERT(varbinary(4), 'ZZ', 2) AS b;")).ToString());
        Assert.Equal("b: null", (await Run("SELECT TRY_CONVERT(varbinary(4), '0xZZ', 1) AS b;")).ToString());
        Assert.Equal("b: 0x0A0B", (await Run("SELECT CONVERT(varchar(10), CONVERT(varbinary(4), '0A0B', 2), 1) AS b;")).ToString());

        // Out of binary into text a style writes hex, cut short to whole bytes where the text is too short, never NULL.
        Assert.Equal("a,b: 0x,01", (await Run("SET ANSI_WARNINGS OFF; SET ARITHABORT OFF; SELECT CONVERT(varchar(3), 0x0102030405, 1) AS a, CONVERT(nvarchar(3), 0x0102030405, 2) AS b;")).ToString());
    }

    // ---- combining types -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Binary_values_combine_as_strings_do_varying_if_either_is_as_long_as_the_longer_max_if_either_is()
    {
        // COALESCE, CASE, IIF and UNION ALL alike; a rowversion with a rowversion is a timestamp. A binary value beside text, a
        // number or a GUID takes that kind's type (o5 to o9), not modelled.
        Assert.Equal(
            "o1 varbinary(10), o2 binary(10), o3 varbinary(12), o4 varbinary(max), o5 varchar(3), o6 nvarchar(10), o7 int, o8 timestamp, o9 timestamp, c1 varbinary(10), c2 varbinary(12), r timestamp",
            await Describe($"""
                SELECT COALESCE(v2, b10) AS o1, COALESCE(b2, b10) AS o2, COALESCE(b12, v10) AS o3, COALESCE(b2, vm) AS o4, COALESCE(v2, a) AS o5,
                       COALESCE(v10, nb) AS o6, COALESCE(v10, i) AS o7, COALESCE(rv, v2) AS o8, COALESCE(v2, rv) AS o9,
                       CASE WHEN i = 1 THEN v2 ELSE b10 END AS c1, IIF(i = 1, b12, v10) AS c2, COALESCE(rv, rv) AS r
                FROM {Row}
                """));
        Assert.Equal("u varbinary(10)", await Describe($"SELECT b2 AS u FROM {Row} UNION ALL SELECT v10 FROM {Row}"));
    }

    [Fact]
    public async Task Isnull_cuts_or_pads_a_binary_second_argument_to_the_firsts_type()
    {
        Assert.Equal("i1 binary(2), i2 varbinary(2), i3 binary(10), i4 timestamp", await Describe($"SELECT ISNULL(b2, b10) AS i1, ISNULL(v2, b10) AS i2, ISNULL(b10, v2) AS i3, ISNULL(rv, v10) AS i4 FROM {Row}"));
        Assert.Equal(
            "i1,i2,i3: 0x0102,0x0100,0x0102",
            (await Run("SELECT CONVERT(varchar(20), ISNULL(CAST(NULL AS binary(2)), 0x010203), 1) AS i1, CONVERT(varchar(20), ISNULL(CAST(NULL AS binary(2)), 0x01), 1) AS i2, CONVERT(varchar(20), ISNULL(CAST(NULL AS varbinary(2)), 0x010203), 1) AS i3;")).ToString());
    }

    // ---- concatenation -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Binary_plus_is_as_long_as_both_up_to_8000_bytes_and_fixed_only_when_both_are()
    {
        // Varying if either is; past 8000 bytes it is cut there; max if either is. NULL when either is, unless the caller's session
        // sets CONCAT_NULL_YIELDS_NULL off, as for strings.
        Assert.Equal(
            "p1 varbinary(12), p2 binary(22), p3 binary(8000), p4 varbinary(8000), p5 varbinary(max), p6 varbinary(3)",
            await Describe($"SELECT v2 + b10 AS p1, b10 + b12 AS p2, b5k + b5k AS p3, v5k + v5k AS p4, vm + v2 AS p5, 0x01 + 0x0203 AS p6 FROM {Row}"));
        Assert.Equal("cut: 8000", (await Run("SELECT DATALENGTH(CAST(0x01 AS binary(5000)) + CAST(0x01 AS binary(5000))) AS cut;")).ToString());
        Assert.Equal("on: null", (await Run("SELECT CONVERT(varchar(10), CAST(NULL AS varbinary(2)) + 0x01, 1) AS [on];")).ToString());
        Assert.Equal("off: 0x01", (await Run("SET CONCAT_NULL_YIELDS_NULL OFF; SELECT CONVERT(varchar(10), CAST(NULL AS varbinary(2)) + 0x01, 1) AS [off];")).ToString());
    }

    [Fact]
    public async Task A_rowversion_under_plus_or_concat_counts_as_40_bytes() =>
        // Not the 8 it holds, so neither is modelled.
        Assert.Equal("p1 varbinary(42), p2 varbinary(80), k varchar(52)", await Describe($"SELECT rv + v2 AS p1, rv + rv AS p2, CONCAT(rv, 1) AS k FROM {Row}"));

    [Fact]
    public async Task Concat_reads_a_binary_value_as_varchar_of_its_bytes()
    {
        // Never null. Beside nvarchar it is nvarchar, a binary value counting half its bytes rounded up, its bytes read as UTF-16:
        // not modelled.
        Assert.Equal(
            "k1 varchar(3), k2 varchar(2), k3 varchar(max), k4 varchar(13), k5 nvarchar(3), k6 nvarchar(4)",
            await Describe("SELECT CONCAT(0x01, 0x0203) AS k1, CONCAT(0x01, 'a') AS k2, CONCAT(CAST(0x01 AS varbinary(max)), 0x01) AS k3, CONCAT_WS(0x2C, CAST(0x01 AS varbinary(2)), CAST(0x01 AS binary(10))) AS k4, CONCAT(CAST(0x414243 AS binary(3)), N'a') AS k5, CONCAT(CAST(0x41 AS varbinary(5)), N'a') AS k6"));
        Assert.Equal("k,n: AB!,0", (await Run("SELECT CONCAT(0x4142, CAST(NULL AS varbinary(2)), 0x21) AS k, CASE WHEN CONCAT(CAST(NULL AS varbinary(2)), CAST(NULL AS varbinary(2))) IS NULL THEN 1 ELSE 0 END AS n;")).ToString());
    }

    // ---- parts and lengths ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Substring_of_a_binary_value_is_varbinary_and_left_and_right_varchar_as_long_as_a_literal_length_allows() =>
        Assert.Equal(
            "s1 varbinary(2), s2 varbinary(3), s3 varbinary(max), s4 varbinary(10), l1 varchar(2), l2 varchar(2), l3 varchar(max), l4 varchar(10)",
            await Describe($"SELECT SUBSTRING(0x010203, 1, 2) AS s1, SUBSTRING(b10, 2, 3) AS s2, SUBSTRING(vm, 1, 2) AS s3, SUBSTRING(v10, 1, i) AS s4, LEFT(0x010203, 2) AS l1, RIGHT(b10, 2) AS l2, LEFT(vm, 3) AS l3, LEFT(v10, i) AS l4 FROM {Row}"));

    [Fact]
    public async Task Datalength_of_a_binary_value_is_int_and_bigint_over_max() =>
        Assert.Equal(
            "d1 int, d2 bigint, d3 int, d4 int",
            await Describe($"SELECT DATALENGTH(v10) AS d1, DATALENGTH(vm) AS d2, DATALENGTH(rv) AS d3, DATALENGTH(CAST(0x01 AS image)) AS d4 FROM {Row}"));

    [Fact]
    public async Task Max_and_min_keep_a_binary_or_rowversion_type_and_refuse_an_image()
    {
        Assert.Equal("m1 varbinary(10), m2 timestamp, m3 binary(10)", await Describe($"SELECT MAX(v10) AS m1, MIN(rv) AS m2, MAX(b10) AS m3 FROM {Row}"));
        Assert.Equal(" ! 8117", (await Run("SELECT MAX(s) AS m FROM #s;", "CREATE TABLE #s (s image NULL);")).ToString());
    }

    // ---- query files ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_parameter_compared_with_a_binary_column_of_any_kind_takes_its_type_under_equality_and_varbinary_under_ordering()
    {
        // QueryParameterObservationTests observes a varbinary(16) beside haruspex's answer; these are the other kinds: a fixed
        // length and a rowversion under equality, and ordering over a fixed length and over max, which stays max.
        await server.RequireAsync();
        var token = TestContext.Current.CancellationToken;
        await using var session = await server.WriteAsync(token);
        // A table of the session's own transaction, rolled back: the procedure can't see a temporary one.
        await using (var create = session.Command("CREATE TABLE dbo.obs_binary_params (id int NOT NULL, b32 binary(32) NOT NULL, vm varbinary(max) NULL, rv rowversion);"))
            await create.ExecuteNonQueryAsync(token);
        await using var describe = session.Command("EXEC sys.sp_describe_undeclared_parameters @tsql = N'SELECT b.id FROM dbo.obs_binary_params AS b WHERE b.b32 = @a AND b.rv = @b AND b.b32 < @c AND b.vm > @d AND b.vm = @e';");
        var types = new List<string>();
        await using var reader = await describe.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
            types.Add(reader.GetString(reader.GetOrdinal("name")) + " " + reader.GetString(reader.GetOrdinal("suggested_system_type_name")));

        Assert.Equal(["@a binary(32)", "@b timestamp", "@c varbinary(8000)", "@d varbinary(max)", "@e varbinary(max)"], types);
    }

    // ---- functions -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Hashbytes_is_varbinary_8000_of_its_algorithms_length_and_null_for_an_algorithm_it_does_not_run()
    {
        // Described as varbinary(8000) whatever the algorithm; NULL for NULL input, for MD2, for a name it doesn't know and for one
        // with a leading space; the name in any case, trailing spaces ignored.
        Assert.Equal("h1 varbinary(8000), h2 varbinary(8000), h3 varbinary(8000)", await Describe("SELECT HASHBYTES('MD5', 'a') AS h1, HASHBYTES('SHA2_512', 0x01) AS h2, HASHBYTES(N'SHA1', N'a') AS h3"));
        Assert.Equal(
            "md2,unknown,spaced,nul,trailing,lower: null,null,null,null,32,32",
            (await Run("SELECT DATALENGTH(HASHBYTES('MD2', 'a')) AS md2, DATALENGTH(HASHBYTES('XYZ', 'a')) AS unknown, DATALENGTH(HASHBYTES(' MD5', 'a')) AS spaced, DATALENGTH(HASHBYTES('MD5', CAST(NULL AS varchar(1)))) AS nul, DATALENGTH(HASHBYTES('SHA2_256 ', 'a')) AS trailing, DATALENGTH(HASHBYTES('sha2_256', 'a')) AS lower;")).ToString());

        // Each algorithm's length at every compatibility level the server offers, and over an input past 8000 bytes.
        await using var scratch = await server.ScratchAsync();
        var token = TestContext.Current.CancellationToken;
        foreach (var level in (int[])[100, 110, 120, 130, 140, 150, 160, 170])
        {
            await scratch.ExecuteAsync($"ALTER DATABASE CURRENT SET COMPATIBILITY_LEVEL = {level.ToString(CultureInfo.InvariantCulture)};", token);
            await using var connection = await scratch.OpenAsync(token);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT CONCAT_WS(',', ISNULL(CAST(DATALENGTH(HASHBYTES('MD2', 'a')) AS varchar(4)), 'null'), DATALENGTH(HASHBYTES('MD4', 'a')),
                    DATALENGTH(HASHBYTES('MD5', 'a')), DATALENGTH(HASHBYTES('SHA', 'a')), DATALENGTH(HASHBYTES('SHA1', 'a')),
                    DATALENGTH(HASHBYTES('SHA2_256', 'a')), DATALENGTH(HASHBYTES('SHA2_512', REPLICATE(CAST('a' AS varchar(max)), 9000))));
                """;
            Assert.Equal($"{level}: null,16,16,20,20,32,64", $"{level}: {await command.ExecuteScalarAsync(token)}");
        }
    }

    [Fact]
    public async Task Compress_and_decompress_are_varbinary_max_null_only_for_null_and_decompress_of_other_bytes_is_an_error()
    {
        Assert.Equal("c varbinary(max), d varbinary(max)", await Describe("SELECT COMPRESS('abc') AS c, DECOMPRESS(0x01) AS d"));
        Assert.Equal(
            "c,d,e,r: null,null,0,3",
            (await Run("SELECT DATALENGTH(COMPRESS(CAST(NULL AS varbinary(1)))) AS c, DATALENGTH(DECOMPRESS(CAST(NULL AS varbinary(1)))) AS d, DATALENGTH(DECOMPRESS(COMPRESS(0x))) AS e, DATALENGTH(DECOMPRESS(COMPRESS('abc'))) AS r;")).ToString());
        Assert.Equal(9826, (await Run("SELECT DECOMPRESS(0x01) AS d;")).Error);
        Assert.Equal(9826, (await Run("SET ANSI_WARNINGS OFF; SET ARITHABORT OFF; SELECT DECOMPRESS(0x01) AS d;")).Error);
    }

    // ---- columns -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_rowversion_column_declared_without_null_is_not_null_whatever_the_ansi_settings()
    {
        // Neither NULL nor NOT NULL: not null, under ANSI_NULL_DFLT_ON as under OFF. Declared NULL, it is nullable.
        Assert.Equal(
            "a timestamp not null, b timestamp not null, c timestamp null",
            await FirstResultSet.DescribeNullability(server, "SELECT a.r AS a, b.r AS b, c.r AS c FROM #a AS a, #b AS b, #c AS c", [
                "SET ANSI_NULL_DFLT_ON ON; CREATE TABLE #a (r rowversion);",
                "SET ANSI_NULL_DFLT_OFF ON; CREATE TABLE #b (r rowversion);",
                "CREATE TABLE #c (r rowversion NULL);",
            ]));
    }

    [Fact]
    public async Task A_nullable_binary_column_created_with_ansi_padding_off_cuts_trailing_zeros_and_a_not_null_one_pads()
    {
        // The setting is the one in force when the table is created: a script's own SET before its CREATE TABLE, or, for a
        // temporary table a procedure makes, the caller's session. A table variable's column is padded whatever it is.
        var outcome = await Run(
            "SET ANSI_PADDING OFF; EXEC dbo.obs_padded; SET ANSI_PADDING ON; EXEC dbo.obs_padded; INSERT INTO dbo.obs_pad (b, bn) VALUES (0x01, 0x01); SELECT DATALENGTH(b) AS n, DATALENGTH(bn) AS nn FROM dbo.obs_pad;",
            "SET ANSI_PADDING OFF; CREATE TABLE dbo.obs_pad (b binary(4) NULL, bn binary(4) NOT NULL); SET ANSI_PADDING ON;",
            """
            CREATE PROCEDURE dbo.obs_padded AS
            SELECT CAST(CASE WHEN 1 = 1 THEN 0x01 END AS binary(4)) AS b INTO #t;
            DECLARE @v TABLE (b binary(4) NULL);
            INSERT INTO @v VALUES (0x01);
            SELECT (SELECT DATALENGTH(b) FROM #t) AS made, (SELECT DATALENGTH(b) FROM @v) AS variable;
            """);

        Assert.Equal("made,variable: 1,4 | made,variable: 4,4 | n,nn: 1,4", outcome.ToString());
    }

    // ---- JSON ----------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task For_json_writes_a_binary_value_as_padded_base64_on_one_line()
    {
        // The standard alphabet, padded with =, "" for no bytes, a fixed length's zeros included, a rowversion's eight bytes; and the
        // JSON constructors alike.
        Assert.Equal(
            """j: [{"e":"","b1":"AQ==","b2":"AQI=","b3":"AQID","symbols":"+/8=","fixed4":"AQAAAA==","rv":"AAAAAAAAAAE=","n":null}]""",
            (await Run("SELECT (SELECT 0x AS e, 0x01 AS b1, 0x0102 AS b2, 0x010203 AS b3, 0xFBFF AS symbols, CAST(0x01 AS binary(4)) AS fixed4, CAST(0x0000000000000001 AS rowversion) AS rv, CAST(NULL AS varbinary(2)) AS n FOR JSON PATH, INCLUDE_NULL_VALUES) AS j;")).ToString());
        Assert.Equal(
            """o,a: {"a":"AQI="},["AQI=","AQAA"]""",
            (await Run("SELECT JSON_OBJECT('a': 0x0102) AS o, JSON_ARRAY(0x0102, CAST(0x01 AS binary(3))) AS a;")).ToString());

        // Past 8000 bytes: 9000 bytes are 12000 characters, with no line break.
        Assert.Equal(
            "len,lf,cr: 12010,0,0",
            (await Run("SELECT LEN(j) AS len, CHARINDEX(CHAR(10), j) AS lf, CHARINDEX(CHAR(13), j) AS cr FROM (SELECT (SELECT CAST(REPLICATE(CAST(0x41 AS varbinary(max)), 9000) AS varbinary(max)) AS big FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) AS j) AS x;")).ToString());
    }

    [Fact]
    public async Task An_image_value_arrives_as_varbinary_max_s_does() =>
        // Through FOR JSON, base64; MssqlDriverObservationTests has the driver's Buffer.
        Assert.Equal("""j: [{"a":"AQ=="}]""", (await Run("SELECT (SELECT CAST(0x01 AS image) AS a FOR JSON PATH) AS j;")).ToString());

    [Fact]
    public async Task Openjson_reads_a_binary_column_from_base64_and_refuses_any_other_text()
    {
        // A value that isn't base64, or that is longer than the column, is an error (13612, 13613), never NULL.
        Assert.Equal("b: 0x0102", (await Run("SELECT CONVERT(varchar(10), b, 1) AS b FROM OPENJSON(N'{\"b\":\"AQI=\"}') WITH (b varbinary(10) '$.b');")).ToString());
        Assert.Equal(13612, (await Run("SELECT b FROM OPENJSON(N'{\"b\":\"AQI\"}') WITH (b varbinary(10) '$.b');")).Error);
        Assert.Equal(13612, (await Run("SELECT b FROM OPENJSON(N'{\"b\":12}') WITH (b varbinary(10) '$.b');")).Error);
        Assert.Equal(13613, (await Run("SELECT b FROM OPENJSON(N'{\"b\":\"AQIDBAUGBwgJCgsM\"}') WITH (b varbinary(4) '$.b');")).Error);
    }
}
