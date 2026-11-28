using System.Globalization;
using Haruspex.DifferentialTests.Server;
using Haruspex.SqlServer.Inference;
using Microsoft.Data.SqlClient;

namespace Haruspex.DifferentialTests;

/// <summary>
/// What text of a <c>varchar</c> or <c>char</c> type gives back of a string, which the reads of JSON held in such text rest on
/// (<c>CodePageText</c>; docs/decisions/0145-varchar-json-is-read-through-its-code-page.md): which characters each code page keeps,
/// that every UTF-16 unit comes back as one character, that a JSON escape is kept where the character written as itself is not, and
/// which collation a column, a parameter and <c>OPENJSON … WITH</c> take. Observed on SQL Server 2025 RTM-CU10.
/// </summary>
public class CodePageObservationTests(SqlServerFixture server)
{
    /// <summary>A collation of each code page SQL Server has, and one of UTF-8.</summary>
    public static TheoryData<string> CodePages =>
    [
        "SQL_Latin1_General_CP437_BIN", "SQL_Latin1_General_CP850_BIN", "Thai_100_BIN2", "Japanese_XJIS_100_BIN2",
        "Chinese_Simplified_Pinyin_100_BIN2", "Korean_100_BIN2", "Chinese_Traditional_Stroke_Order_100_BIN2", "Albanian_100_BIN2",
        "Cyrillic_General_100_BIN2", "Latin1_General_100_BIN2", "SQL_Latin1_General_CP1_CI_AS", "Greek_100_BIN2", "Turkish_100_BIN2",
        "Hebrew_100_BIN2", "Arabic_100_BIN2", "Estonian_100_BIN2", "Vietnamese_100_BIN2", "Latin1_General_100_BIN2_UTF8",
    ];

    /// <summary>
    /// Every character of the Basic Multilingual Plane, but the surrogates, through <paramref name="collation"/>'s code page and
    /// back: it is kept exactly when <c>CodePageText.Holds</c> says (.NET's encoding of the code page, with no fallback), and
    /// whatever it becomes is one UTF-16 unit. A pair outside the plane comes back as two units, and a lone surrogate as one.
    /// </summary>
    [Theory]
    [MemberData(nameof(CodePages))]
    public async Task A_character_survives_a_code_page_exactly_when_its_encoding_holds_it_and_every_unit_comes_back_as_one(string collation)
    {
        var token = TestContext.Current.CancellationToken;
        await using var session = await server.ReadAsync(token);
        await using var command = session.Command($"""
            SELECT s.value, CASE WHEN UNICODE(x.back) = s.value AND DATALENGTH(x.back) = 2 THEN 1 ELSE 0 END, DATALENGTH(x.back) / 2
            FROM GENERATE_SERIES(0, 65535) AS s
            CROSS APPLY (SELECT CAST(CAST(NCHAR(s.value) COLLATE {collation} AS varchar(4)) AS nvarchar(4))) AS x(back)
            WHERE s.value NOT BETWEEN 55296 AND 57343;
            SELECT DATALENGTH(CAST(CAST(NCHAR(55357) + NCHAR(56832) + NCHAR(55358) + NCHAR(56611) COLLATE {collation} AS varchar(16)) AS nvarchar(16))) / 2,
                DATALENGTH(CAST(CAST(NCHAR(55357) COLLATE {collation} AS varchar(16)) AS nvarchar(16))) / 2;
            """);
        await using var reader = await command.ExecuteReaderAsync(token);
        var (differ, units, count) = (new List<string>(), new List<int>(), 0);
        while (await reader.ReadAsync(token))
        {
            count++;
            var (code, kept) = (reader.GetInt32(0), reader.GetInt32(1) == 1);
            if (kept != CodePageText.Holds(collation, ((char)code).ToString()))
                differ.Add(code.ToString("X4", CultureInfo.InvariantCulture));
            if (reader.GetInt32(2) != 1)
                units.Add(code);
        }

        Assert.True(await reader.NextResultAsync(token) && await reader.ReadAsync(token));
        var (pairs, lone) = (reader.GetInt32(0), reader.GetInt32(1));

        Assert.Equal(65536 - 2048, count);
        Assert.Empty(differ);
        Assert.Empty(units);
        Assert.Equal((4, 1), (pairs, lone));
    }

    private static readonly string[] Columns = ["cp1252", "cp1251", "fixed", "utf8", "wide"];

    /// <summary>
    /// JSON of the same strings in columns of several collations, each string once as itself and once as a JSON escape (built with
    /// <c>NCHAR(92)</c>, a backslash): the escape is ASCII, so it is kept, and <c>json_value</c>, <c>OPENJSON</c> and
    /// <c>OPENJSON … WITH</c> into <c>nvarchar</c> give the character back; written as itself, the code page decides. Ω is
    /// <c>O</c> under 1252 and <c>?</c> under 1251, é is kept under 1252 and <c>e</c> under 1251, Ж the other way about, and 😀 is
    /// <c>?</c> (63, the first of <c>??</c>) under every code page but UTF-8. <c>UNICODE</c> gives a pair's code point under a
    /// supplementary-character (<c>_SC</c>) collation, as the UTF-8 one is, and its high surrogate (55357) under any other. A <c>char(n)</c> column is read as a <c>varchar</c> one is.
    /// </summary>
    [Fact]
    public async Task Json_in_a_varchar_column_comes_back_through_its_code_page_unless_it_is_escaped()
    {
        string[] setup =
        [
            """
            CREATE TABLE dbo.obs_badge (
                cp1252 varchar(400) COLLATE SQL_Latin1_General_CP1_CI_AS, cp1251 varchar(400) COLLATE Cyrillic_General_CI_AS,
                fixed char(400) COLLATE SQL_Latin1_General_CP1_CI_AS, utf8 varchar(400) COLLATE Latin1_General_100_CI_AS_SC_UTF8,
                wide nvarchar(400));
            """,
            """
            DECLARE @doc nvarchar(400) = REPLACE(
                N'{"omega":"Ω","acute":"é","zhe":"Ж","face":"😀","omega_e":"#u03a9","acute_e":"#u00e9","zhe_e":"#u0416","face_e":"#ud83d#ude00"}',
                N'#', NCHAR(92));
            INSERT INTO dbo.obs_badge VALUES (@doc, @doc, @doc, @doc, @doc);
            """,
        ];
        static string Reads(string column) =>
            $"""
            SELECT '{column}' AS col,
                UNICODE(json_value({column}, '$.omega')) AS omega, UNICODE(json_value({column}, '$.acute')) AS acute,
                UNICODE(json_value({column}, '$.zhe')) AS zhe, UNICODE(json_value({column}, '$.face')) AS face,
                UNICODE(json_value({column}, '$.omega_e')) AS omega_e, UNICODE(json_value({column}, '$.acute_e')) AS acute_e,
                UNICODE(json_value({column}, '$.zhe_e')) AS zhe_e, UNICODE(json_value({column}, '$.face_e')) AS face_e,
                UNICODE(w.omega) AS with_omega, UNICODE(w.omega_e) AS with_omega_e,
                (SELECT UNICODE(o.value) FROM OPENJSON({column}) AS o WHERE o.[key] = 'omega_e') AS openjson_omega_e
            FROM dbo.obs_badge
            CROSS APPLY OPENJSON({column}) WITH (omega nvarchar(20) '$.omega', omega_e nvarchar(20) '$.omega_e') AS w;
            """;

        var outcome = await ObservedCall.Run(server, setup, string.Join("\n", Columns.Select(Reads)));

        Assert.Null(outcome.Error);
        const string Names = "col,omega,acute,zhe,face,omega_e,acute_e,zhe_e,face_e,with_omega,with_omega_e,openjson_omega_e";
        Assert.Equal(
            [
                $"{Names}: cp1252,79,233,63,63,937,233,1046,55357,79,937,937",
                $"{Names}: cp1251,63,101,1046,63,937,233,1046,55357,63,937,937",
                $"{Names}: fixed,79,233,63,63,937,233,1046,55357,79,937,937",
                $"{Names}: utf8,937,233,1046,128512,937,233,1046,128512,937,937,937",
                $"{Names}: wide,937,233,1046,55357,937,233,1046,55357,937,937,937",
            ],
            outcome.Sets);
    }

    /// <summary>
    /// A key folds as a value does: <c>{"Ω":"x","O":1}</c> in a 1252 column is <c>{"O":"x","O":1}</c>, so a path to <c>Ω</c> finds
    /// nothing, a path to <c>O</c> finds the first of the two, <c>x</c>, and <c>OPENJSON</c> gives two keys <c>O</c>. Under Greek
    /// (1253), which holds Ω, it is kept.
    /// </summary>
    [Fact]
    public async Task A_key_the_code_page_lacks_comes_back_as_another_key_of_its_length()
    {
        string[] setup =
        [
            "CREATE TABLE dbo.obs_keyed (latin varchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS, greek varchar(100) COLLATE Greek_CI_AS);",
            """INSERT INTO dbo.obs_keyed VALUES (N'{"Ω":"x","O":1}', N'{"Ω":"x","O":1}');""",
        ];

        var outcome = await ObservedCall.Run(server, setup, """
            SELECT json_value(latin, N'$."Ω"') AS omega, json_value(latin, '$.O') AS o, (SELECT COUNT(*) FROM OPENJSON(latin) WHERE [key] = 'O') AS os,
                json_value(greek, N'$."Ω"') AS greek_omega, json_value(greek, '$.O') AS greek_o
            FROM dbo.obs_keyed;
            """);

        Assert.Equal("omega,o,os,greek_omega,greek_o: null,x,2,x,1", outcome.ToString());
    }

    /// <summary>
    /// In a database of a Cyrillic collation (code page 1251), a column with no <c>COLLATE</c>, a <c>varchar</c> parameter and an
    /// <c>OPENJSON … WITH</c> column of <c>varchar</c> all take the database's: é comes back as <c>e</c> and Ж is kept.
    /// </summary>
    [Fact]
    public async Task A_column_a_parameter_and_openjson_with_take_the_database_s_collation()
    {
        var token = TestContext.Current.CancellationToken;
        await using var scratch = await server.ScratchAsync("Cyrillic_General_CI_AS");
        await scratch.ExecuteAsync("CREATE TABLE dbo.obs_badge (body varchar(200));", token);
        await scratch.ExecuteAsync("INSERT INTO dbo.obs_badge VALUES (N'{\"k\":\"é\",\"d\":\"Ж\"}');", token);
        await scratch.ExecuteAsync("CREATE PROCEDURE dbo.obs_param @doc varchar(max) AS SELECT UNICODE(json_value(@doc, '$.k')) AS k, UNICODE(json_value(@doc, '$.d')) AS d;", token);
        await using var connection = await scratch.OpenAsync(token);
#pragma warning disable CA2100 // Observation tests' own batches.
        await using var call = new SqlCommand(
            """
            SELECT UNICODE(json_value(body, '$.k')) AS k, UNICODE(json_value(body, '$.d')) AS d FROM dbo.obs_badge;
            EXEC dbo.obs_param @doc = N'{"k":"é","d":"Ж"}';
            SELECT UNICODE(w.k) AS k, UNICODE(w.d) AS d FROM OPENJSON(N'{"k":"é","d":"Ж"}') WITH (k varchar(10), d varchar(10)) AS w;
            """,
            connection);
#pragma warning restore CA2100

        Assert.Equal("k,d: 101,1046 | k,d: 101,1046 | k,d: 101,1046", (await ObservedCall.Read(call, errorsAsMessages: false, token)).ToString());
    }
}
