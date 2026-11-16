using System.Text;
using Haruspex.Cli;
using Haruspex.Emit.JsonSchema;
using Haruspex.Emit.Mssql;
using Haruspex.Emit.Zod;
using Haruspex.Ir;
using Haruspex.Tests.Snapshotting;

namespace Haruspex.Tests.Diagnostics;

/// <summary>
/// Every code is reported by some input, and its text is what a user reads: each case below makes one code happen, through
/// the step that reports it (the CLI, for all but what only a hand-written IR can cause), and the lines are a committed
/// snapshot, read as carefully as code. The pages in <c>docs/diagnostics</c> show the same lines as their examples
/// (<c>DiagnosticPagesTests</c>). A code no case makes happen fails here, so a code can't be declared and never seen.
/// </summary>
public class DiagnosticMessagesTests
{
    /// <summary>A small invented library: a table of books, with a constraint, a computed column and a trigger-free audit.</summary>
    private const string Tables = """
        CREATE TABLE lib.book (
            id int NOT NULL PRIMARY KEY,
            title nvarchar(100) NOT NULL,
            note nvarchar(200) NULL,
            status varchar(10) NOT NULL CHECK (status IN ('open', 'lent')),
            copies int NOT NULL CHECK (copies IN (1, 2)),
            extra nvarchar(max) NULL,
            pages AS id * 2);
        """;

    private const string GetBook = "CREATE PROCEDURE lib.get_book AS SELECT b.id, b.title, b.note, b.status, b.copies, b.extra FROM lib.book AS b;";

    private const string Plain = "\"source\": \"db\", \"out\": \"out\", \"schemas\": { \"lib\": { \"include\": [\"*\"] } }";

    /// <summary>A run of the CLI on a project of <paramref name="sql"/> (one file each), with the arguments, or with the config file and none.</summary>
    private static string[] Cli(string[] sql, string? config = null, string[]? args = null, Action<TempDirectory>? arrange = null)
    {
        using var folder = new TempDirectory();
        folder.File("db/tables.sql", Tables);
        for (var i = 0; i < sql.Length; i++)
            folder.File($"db/p{i + 1}.sql", sql[i]);
        if (config is not null)
            folder.File("haruspex.json", config);
        arrange?.Invoke(folder);
        using var output = new StringWriter();
        using var error = new StringWriter();
        HaruspexCli.Run(["generate", .. args ?? (config is null ? ["db", "--include", "*.*", "--out", "out"] : [])], output, error, folder.Path);
        return CliOutput.Shown(error.ToString(), folder.Path).TrimEnd('\n').Split('\n');
    }

    /// <summary>
    /// A run of <c>haruspex check</c> on a project of <paramref name="sql"/> and the config file of the standard settings and
    /// <paramref name="more"/>, generated first and then changed by <paramref name="change"/>, as a database change or a hand
    /// would change it.
    /// </summary>
    private static string[] Checked(string[] sql, Action<TempDirectory> change, string more = "")
    {
        using var folder = new TempDirectory();
        folder.File("db/tables.sql", Tables);
        for (var i = 0; i < sql.Length; i++)
            folder.File($"db/p{i + 1}.sql", sql[i]);
        folder.File("haruspex.json", $"{{ {Plain}{(more.Length == 0 ? "" : ", " + more)} }}");
        using var output = new StringWriter();
        using var error = new StringWriter();
        HaruspexCli.Run(["generate"], output, error, folder.Path);
        change(folder);
        error.GetStringBuilder().Clear();
        HaruspexCli.Run(["check"], output, error, folder.Path);
        return CliOutput.Shown(error.ToString(), folder.Path).TrimEnd('\n').Split('\n');
    }

    /// <summary>
    /// A run of the CLI on one of the test DACPACs (<c>tests/dacpacs</c>), copied alone into a folder of its own, so that what it
    /// refers to isn't found beside it, selecting <paramref name="include"/>.
    /// </summary>
    private static string[] Package(string group, string package, string include) =>
        Cli([], args: [package, "--include", include, "--out", "out"], arrange: f => File.Copy(Path.Combine(RepositoryPaths.Root, "tests", "dacpacs", group, package), Path.Combine(f.Path, package)));

    /// <summary>
    /// A run of the CLI on the project file <c>db/db.sqlproj</c>, a <c>Microsoft.Build.Sql</c> project of the standard tables and
    /// <see cref="GetBook"/> whose properties and items are <paramref name="inside"/>.
    /// </summary>
    private static string[] Project(string inside, string sdk = "Microsoft.Build.Sql/2.3.0") =>
        Cli([GetBook], args: ["db/db.sqlproj", "--include", "*.*", "--out", "out"], arrange: f => f.File("db/db.sqlproj", $"""
            <Project Sdk="{sdk}">
              <PropertyGroup>
                <DSP>Microsoft.Data.Tools.Schema.Sql.Sql170DatabaseSchemaProvider</DSP>
              </PropertyGroup>
              {inside}
            </Project>
            """));

    /// <summary>A run with <paramref name="sql"/> and a config file of the standard settings and <paramref name="more"/>.</summary>
    private static string[] Config(string more, params string[] sql) => Cli(sql.Length == 0 ? [GetBook] : sql, $"{{ {Plain}{(more.Length == 0 ? "" : ", " + more)} }}");

    /// <summary>A run whose config file names the query file <c>sql/q.sql</c>, which holds <paramref name="query"/>, beside the standard settings.</summary>
    private static string[] Query(string query) => Cli([GetBook], $"{{ {Plain}, \"queries\": \"sql\" }}", arrange: f => f.File("sql/q.sql", query));

    /// <summary>A run whose config file declares the results of <c>lib.p</c> as <paramref name="results"/>.</summary>
    private static string[] Declares(string results) =>
        Config($"\"procedures\": {{ \"lib.p\": {{ \"results\": {results}, \"why\": \"its SQL is built as it runs\" }} }}",
            "CREATE PROCEDURE lib.p @sql nvarchar(max) AS EXEC sp_executesql @sql;");

    /// <summary>A run whose config file overrides <c>lib.get_book</c>'s path with <paramref name="setting"/>.</summary>
    private static string[] Override(string path, string setting, params string[] sql) =>
        Config($"\"procedures\": {{ \"lib.get_book\": {{ \"paths\": {{ \"{path}\": {setting} }} }} }}", sql.Length == 0 ? [GetBook] : sql);

    /// <summary>What the Zod emitter refuses in an IR written by hand, as the CLI would write it.</summary>
    private static IEnumerable<string> Emitted(IrDocument document) => ZodEmitter.Emit(document).Diagnostics.Select(d => d.ToString());

    private static IrDocument Document(Dictionary<string, IrObjectType> types, params IrObject[] objects) => new(new IrSource("tsql"), [.. objects], types, []);

    private static IrObject Rows(string id, string row) =>
        new(id, id.Split('.')[1], IrObjectKind.Procedure, "p.sql:1:1", [], [new IrResult(IrEncoding.Rowset, new IrArray(new IrRef(row)), [])]);

    private static IrObjectType Row(IrType type) => new([new IrField("v", type, IrPresence.Always, [])]);

    private static readonly IrScalar Number = new(IrScalarKind.Number, IrFormat.Int32, "int");

    /// <summary>Each code, and what makes it happen.</summary>
    private static readonly (int Code, Func<IEnumerable<string>> Run)[] Cases =
    [
        (1001, () => Cli(["CREATE PROCEDURE lib.p AS SELECT FROM;"])),
        (1002, () => Cli([GetBook, GetBook])),
        (1003, () => Package("harbour", "crane.dacpac", "dock.*")),
        (1004, () => Package("atelier", "atelier.dacpac", "studio.list_pots")),
        (1005, () => Cli([GetBook], args: ["broken.dacpac", "--include", "*.*", "--out", "out"], arrange: f => f.File("broken.dacpac", "not a zip"))),
        (1007, () => Project("<ItemGroup><ProjectReference Include=\"..\\shelf\\shelf.sqlproj\" /></ItemGroup>")),
        (1008, () => Project("<PropertyGroup><DefaultCollation>$(LibraryCollation)</DefaultCollation></PropertyGroup>")),
        (1009, () => Project("<ItemGroup><Build Include=\"..\\archive\\loans.sql\" /></ItemGroup>")),
        (1010, () => Project("", sdk: "MSBuild.Sdk.SqlProj/3.0.0")),
        (1012, () => Project("<ItemGroup><ProjectReference Include=\"..\\archive\\archive.sqlproj\"><DatabaseSqlCmdVariable>Archive</DatabaseSqlCmdVariable><ServerSqlCmdVariable>Annexe</ServerSqlCmdVariable></ProjectReference></ItemGroup>")),
        (1013, () => Cli(["CREATE TABLE lib.scan WITH (DISTRIBUTION = ROUND_ROBIN) AS SELECT b.id, b.title FROM lib.book AS b;", GetBook])),
        (1014, () => Query("SELECT 1 AS one;\nGO\nSELECT 2 AS two;\n")),
        (1015, () => Query("-- @param @client_id int\nSELECT b.title FROM lib.book AS b WHERE b.id = @customer_id;\n")),

        (2001, () => Cli(["CREATE PROCEDURE lib.p AS SET NOCOUNT, NOEXEC ON;\nSELECT b.id FROM lib.book AS b;"])),
        (2003, () => Cli(["CREATE PROCEDURE lib.p AS SELECT b.id FROM lib.book AS b;", "ALTER PROCEDURE lib.p AS SELECT b.title AS id FROM lib.book AS b;"])),
        (2004, () => Cli(["CREATE PROCEDURE lib.p AS SELECT b.id FROM lib.book AS b FOR JSON AUTO;"])),
        (2005, () => Cli(["CREATE PROCEDURE lib.p AS SELECT here.lib.book.id AS c FROM lib.book;"])),
        (2006, () => Cli(["CREATE PROCEDURE lib.p AS SELECT s.value FROM STRING_SPLIT(N'a,b', N',') AS s;"])),
        (2007, () => Cli(["CREATE PROCEDURE lib.p AS SELECT b.pages FROM lib.book AS b;"])),
        (2008, () => Cli(["CREATE PROCEDURE lib.p AS SELECT x.a FROM lib.missing AS x;"])),
        (2009, () => Cli(["CREATE PROCEDURE lib.p AS SELECT b.nope AS gone FROM lib.book AS b;"])),
        (2010, () => Cli(["CREATE PROCEDURE lib.p AS SELECT (b.id) FROM lib.book AS b;"])),
        (2011, () => Cli(["CREATE PROCEDURE lib.p AS SELECT b.id AS a, b.title AS a FROM lib.book AS b FOR JSON PATH;"])),
        (2012, () => Cli(["CREATE FUNCTION lib.again(@n int) RETURNS nvarchar(max) AS BEGIN RETURN json_query(lib.again(@n)); END", "CREATE PROCEDURE lib.p AS SELECT json_query(lib.again(b.id)) AS v FROM lib.book AS b FOR JSON PATH;"])),
        (2013, () => Cli(["CREATE PROCEDURE lib.p AS SELECT json_query(b.extra) AS data FROM lib.book AS b FOR JSON PATH;"])),
        (2014, () => Cli(["CREATE PROCEDURE lib.p AS SELECT t.id FROM #caller AS t;"])),
        (2015, () => Cli(["CREATE PROCEDURE lib.p @x int AS WHILE @x > 0 BEGIN SELECT b.id FROM lib.book AS b; SET @x = 0; END"])),
        (2021, () => Cli(["CREATE TRIGGER lib.book_seen ON lib.book AFTER UPDATE AS SELECT i.id FROM inserted AS i;", "CREATE PROCEDURE lib.p AS UPDATE lib.book SET note = NULL;"])),
        (2022, () => Cli(["CREATE PROCEDURE lib.p AS DECLARE c CURSOR LOCAL FOR SELECT b.id FROM lib.book AS b; OPEN c; FETCH NEXT FROM c; CLOSE c; DEALLOCATE c;"])),
        (2023, () => Cli(["CREATE PROCEDURE lib.p AS SELECT '$(Region)' AS region;"])),
        (2024, () => Query("SELECT @label AS label;")),
        (2101, () => Cli(["CREATE PROCEDURE lib.p AS INSERT INTO audit.trail (id) VALUES (1);"])),

        (3001, () => Cli([GetBook], $"{{ {Plain}, \"procedures\": {{ \"lib.get_book\": {{ \"results\": {{ \"rowset\": \"schemas\" }}, \"why\": \"w\" }} }} }}", arrange: f => Directory.CreateDirectory(Path.Combine(f.Path, "schemas")))),
        (3002, () => Cli([GetBook], "{ \"source\": ")),
        (3003, () => Config("\"objects\": \"strip\", \"objects\": \"strict\"")),
        (3004, () => Config("\"colour\": \"blue\"")),
        (3005, () => Config("\"objects\": \"loose\"")),
        (3006, () => Cli([GetBook], "{ \"source\": \"db\", \"out\": \"out\", \"schemas\": { \"lib\": { \"include\": [\"[get\"] } } }")),
        (3007, () => Config("\"procedures\": { \"lib.get_*\": { \"name\": \"Books\" } }")),
        (3008, () => Cli([GetBook], "{ \"source\": \"db\", \"out\": \"out\", \"schemas\": { \"lib\": { \"include\": [\"*\"] }, \"LIB\": { \"include\": [\"*\"] } } }")),
        (3009, () => Cli([GetBook], "{ \"source\": \"db\", \"out\": \"out\", \"schemas\": { \"lib\": { \"include\": [\"*\"] }, \"audit\": { \"exclude\": [\"*\"] } } }")),
        (3010, () => Config("\"naming\": { \"replace\": [{ \"pattern\": \"(\", \"with\": \"\" }] }")),
        (3011, () => Override("[].note", "{ \"nullable\": false }")),
        (3012, () => Override("[].extra", "{ \"type\": \"BookExtraSchema\" }")),
        (3013, () => Override("[].title", "{ \"name\": \"T\", \"why\": \"w\" }")),
        (3014, () => Config("\"procedures\": { \"lib.get_book\": { \"results\": \"none\", \"why\": \"w\", \"paths\": { \"[].id\": { \"name\": \"N\" } } } }")),
        (3015, () => Declares("{ \"mayBeAbsent\": true }")),
        (3016, () => Declares("{ \"rowset\": { \"type\": \"object\", \"properties\": { \"a\": { \"type\": \"integer\" } } }, \"mayBeAbsent\": true }")),
        (3017, () => Override("a..b", "{ \"name\": \"X\" }")),
        (3018, () => Override("[].note", "{ \"nullable\": true, \"why\": \"w\" }")),
        (3019, () => Config("\"procedures\": { \"lib.get_book\": { \"name\": \"delete\" } }")),
        (3020, () => Override("[].note", "{ }")),

        (3021, () => Config("\"references\": [\"no/such/folder\"]")),
        (3022, () => Config("\"queries\": \"../shared/sql\"")),
        (3101, () => Declares("{ \"rowset\": { \"type\": \"string\" } }")),
        (3102, () => Declares("{ \"rowset\": { \"type\": \"object\", \"properties\": { \"a\": { \"type\": \"integer\" }, \"b\": { \"type\": \"integer\" } }, \"required\": [\"a\"] } }")),
        (3103, () => Declares("{ \"rowset\": { \"type\": \"object\", \"properties\": { \"a\": { \"type\": \"array\", \"items\": { \"type\": \"integer\" } } } } }")),
        (3104, () => Declares("{ \"json\": { \"type\": [\"array\", \"null\"], \"items\": { \"type\": \"integer\" } } }")),
        (3105, () => Declares("{ \"json\": { \"type\": \"object\", \"properties\": { \"a\": { \"minimum\": 1 } } } }")),
        (3106, () => Declares("{ \"json\": { \"type\": [\"string\", \"integer\"] } }")),
        (3107, () => Declares("{ \"json\": { \"type\": \"date\" } }")),
        (3108, () => Declares("{ \"json\": { \"type\": \"object\" } }")),
        (3109, () => Declares("{ \"json\": { \"type\": \"object\", \"properties\": { \"a\": { \"type\": \"integer\" } }, \"required\": [\"b\"] } }")),
        (3110, () => Declares("{ \"json\": { \"type\": \"object\", \"properties\": { \"a\": { \"type\": [\"integer\", \"null\"] } } } }")),
        (3111, () => Declares("{ \"json\": { \"type\": \"array\" } }")),
        (3112, () => Declares("{ \"json\": { \"$ref\": \"other.json#/$defs/a\" } }")),
        (3113, () => Declares("{ \"json\": { \"$ref\": \"#/$defs/missing\" } }")),
        (3114, () => Declares("{ \"json\": { \"$ref\": \"#/$defs/node\", \"$defs\": { \"node\": { \"type\": \"object\", \"properties\": { \"next\": { \"$ref\": \"#/$defs/node\" } } } } } }")),
        (3150, () => Declares("{ \"json\": { \"type\": \"string\", \"format\": \"ipv4\" } }")),
        (3151, () => Declares("{ \"json\": { \"type\": \"string\", \"pattern\": \"^a\" } }")),
        (3152, () => Declares("{ \"json\": { \"$ref\": \"#/$defs/a\", \"type\": \"string\", \"$defs\": { \"a\": { \"type\": \"string\" } } } }")),

        (4001, () => Cli([GetBook], "{ \"source\": \"db\", \"out\": \"out\", \"schemas\": { \"lib\": { \"include\": [\"*\"] }, \"shelf\": { \"include\": [\"*\"] } } }")),
        (4002, () => Cli([GetBook], "{ \"source\": \"db\", \"out\": \"out\", \"schemas\": { \"lib\": { \"include\": [\"get_*\", \"list_*\"] } } }")),
        (4003, () => Cli([GetBook], "{ \"source\": \"db\", \"out\": \"out\", \"schemas\": { \"lib\": { \"include\": [\"*\"], \"exclude\": [\"*_old\"] } } }")),
        (4004, () => Cli([GetBook], "{ \"source\": \"db\", \"out\": \"out\", \"schemas\": { \"lib\": { \"include\": [\"get_*\"], \"exclude\": [\"get_*\"] } } }")),
        (4005, () => Cli([GetBook], args: ["db", "--include", "lib.list_*", "--out", "out"])),
        (4006, () => Cli([GetBook], "{ \"source\": \"db\", \"out\": \"out\", \"schemas\": { \"lib\": { \"include\": [\"*\"], \"exclude\": [\"get_*\"] } } }", ["--include", "lib.get_book"])),
        (4007, () => Config("\"queries\": \"sql/**/*.query\"")),
        (4101, () => Config("\"procedures\": { \"lib.lost_book\": { \"name\": \"LostBook\" } }")),
        (4102, () => Cli([GetBook, "CREATE PROCEDURE lib.purge AS DELETE FROM lib.book;"], "{ \"source\": \"db\", \"out\": \"out\", \"schemas\": { \"lib\": { \"include\": [\"get_*\"] } }, \"procedures\": { \"lib.purge\": { \"name\": \"Purge\" } } }")),
        (4103, () => Config("\"procedures\": { \"lib.get_book\": { \"name\": \"Books\" }, \"LIB.GET_BOOK\": { \"name\": \"Volumes\" } }")),
        (4104, () => Override("[].nope", "{ \"name\": \"Nope\" }")),
        (4105, () => Override("[].u<json>[].a", "{ \"nullable\": false, \"why\": \"w\" }", "CREATE PROCEDURE lib.get_book AS SELECT json_array(json_object('a': 1), (SELECT b.id FROM lib.book AS b FOR JSON PATH)) AS u;")),
        (4106, () => Override("[].children[]", "{ \"name\": \"Child\" }",
            "CREATE FUNCTION lib.tree(@id int) RETURNS nvarchar(max) AS BEGIN RETURN (SELECT b.id, json_query(lib.tree(b.id)) AS children FROM lib.book AS b WHERE b.id > @id FOR JSON PATH); END",
            "CREATE PROCEDURE lib.get_book AS SELECT b.id, json_query(lib.tree(b.id)) AS children FROM lib.book AS b FOR JSON PATH;")),
        (4107, () => Override("[].one<json>", "{ \"nullable\": false, \"why\": \"w\" }", "CREATE PROCEDURE lib.get_book AS SELECT (SELECT b.id FROM lib.book AS b FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) AS one;")),
        (4108, () => Override("[].title", "{ \"values\": \"exact\", \"why\": \"w\" }")),
        (4109, () => Override("[].title", "{ \"name\": \"Title\" }")),
        (4110, () => Override("[].id", "{ \"nullable\": false, \"why\": \"w\" }")),
        (4111, () => Override("[].copies", "{ \"values\": \"exact\", \"why\": \"w\" }")),
        (4201, () => Config("\"procedures\": { \"lib.get_book\": { \"results\": \"none\", \"why\": \"w\" } }")),
        (4202, () => Config("\"procedures\": { \"lib.count_books\": { \"results\": { \"rowset\": { \"type\": \"object\", \"properties\": { \"n\": { \"type\": [\"integer\", \"null\"] } } } }, \"why\": \"w\" } }", GetBook, "CREATE PROCEDURE lib.count_books AS SELECT COUNT(*) AS n FROM lib.book AS b;")),
        (4203, () => Config("\"procedures\": { \"other.thing\": { \"results\": \"none\", \"why\": \"w\" } }")),
        (4204, () => Cli(["CREATE PROCEDURE lib.p @sql nvarchar(max) AS\nEXEC sp_executesql @sql;"])),
        (4205, () => Cli(["CREATE PROCEDURE lib.p AS\nCREATE TABLE #s (id int NOT NULL);\nINSERT INTO #s EXEC (N'SELECT 1');\nSELECT s.id FROM #s AS s;"])),
        (4301, () => Config("\"naming\": { \"case\": \"preserve\" }", "CREATE PROCEDURE lib.[book#] AS SELECT b.id FROM lib.book AS b;")),
        (4302, () => Config("\"naming\": { \"case\": \"preserve\" }",
            "CREATE FUNCTION lib.[tree#](@id int) RETURNS nvarchar(max) AS BEGIN RETURN (SELECT b.id, json_query(lib.[tree#](b.id)) AS children FROM lib.book AS b WHERE b.id > @id FOR JSON PATH); END",
            "CREATE PROCEDURE lib.GetBook AS SELECT b.id, json_query(lib.[tree#](b.id)) AS children FROM lib.book AS b FOR JSON PATH;")),
        (4303, () => Cli([GetBook, "CREATE PROCEDURE lib.[get_book#] AS SELECT b.id FROM lib.book AS b;"], $"{{ {Plain}, \"naming\": {{ \"replace\": [{{ \"pattern\": \"#$\", \"with\": \"\" }}] }} }}")),
        (4304, () => Config("\"procedures\": { \"lib.get_book\": { \"name\": \"GetBook\" } }")),

        (5001, () => Emitted(Document(new() { ["string"] = Row(Number) }, Rows("s.p", "string")))),
        (5002, () => Emitted(Document(new() { ["Row"] = Row(new IrImport("class", "./a")) }, Rows("s.p", "Row")))),
        (5003, () => Config("\"procedures\": { \"lib.get_book\": { \"paths\": { \"[].title\": { \"type\": \"TextSchema\", \"import\": \"./a\", \"why\": \"w\" }, \"[].note\": { \"type\": \"TextSchema\", \"import\": \"./b\", \"why\": \"w\" } } } }")),
        (5004, () => Override("[].title", "{ \"type\": \"z\", \"import\": \"./a\", \"why\": \"w\" }")),
        (5005, () => Emitted(Document(
            new() { ["Shared"] = Row(Number) },
            Rows("s.rows", "Shared"),
            new IrObject("s.json", "json", IrObjectKind.Procedure, "p.sql:1:1", [], [new IrResult(IrEncoding.Json, new IrRef("Shared"), [])])))),
        (5006, () => Emitted(Document(
            new() { ["GetOrderParams"] = Row(Number) },
            Rows("shop.list_orders", "GetOrderParams"),
            new IrObject("shop.get_order", "GetOrder", IrObjectKind.Procedure, "p.sql:1:1", [new IrParameter("@order_id", Number, false, false)], [])))),
        (5101, () => JsonSchemaEmitter.Emit(Document(
            new() { ["GetBasket"] = Row(Number) },
            new IrObject("shop.get_basket", "GetBasket", IrObjectKind.Procedure, "p.sql:1:1", [], [new IrResult(IrEncoding.Json, new IrRef("GetBasket"), [])]))).Diagnostics.Select(d => d.ToString())),

        (5201, () => MssqlEmitter.Emit(
            Document([], new IrObject("shop.delete", "Delete", IrObjectKind.Procedure, "p.sql:1:1", [], [])),
            new MssqlOptions { IsReserved = TypeScriptNames.IsReserved }).Diagnostics.Select(d => d.ToString())),
        (5202, () => Cli([GetBook], $"{{ {Plain}, \"queries\": \"sql\", \"emit\": [\"zod\", \"mssql\"] }}", arrange: f => f.File("sql/label.sql", "SELECT @label AS label;"))),

        (6001, () => Cli([GetBook], args: ["--colour", "blue"])),
        (6002, () => Cli([GetBook], args: ["db", "--config", "nowhere.json", "--include", "*.*", "--out", "out"])),
        (6003, () => Cli([GetBook], args: ["--include", "*.*", "--out", "out"])),
        (6004, () => Cli([GetBook], args: ["nowhere", "--include", "*.*", "--out", "out"])),
        (6005, () => Cli([GetBook], args: ["db", "--include", "*.*"])),
        (6006, () => Cli([GetBook], args: ["db", "--include", "a.b.c", "--out", "out"])),
        (6007, () => Cli([GetBook], args: ["db", "--out", "out"])),
        (6008, () => Cli([GetBook], arrange: f => f.File("out", "a file where the output folder would be"))),
        (6009, () => Config("\"references\": [\"db\"]")),
        (6010, () => Cli(
            [],
            """{ "source": "atelier.dacpac", "out": "out", "collation": "Latin1_General_100_CI_AS", "schemas": { "studio": { "include": ["list_pots"] } } }""",
            arrange: f => File.Copy(Path.Combine(RepositoryPaths.Root, "tests", "dacpacs", "atelier", "atelier.dacpac"), Path.Combine(f.Path, "atelier.dacpac")))),
        (6011, () => Checked([GetBook], f => Directory.Delete(Path.Combine(f.Path, "out"), recursive: true))),
        (6012, () => Checked([GetBook], f => f.File("out/haruspex.ir.json", "{ \"irVersion\": \"0.9\" }"))),
        (6013, () => Checked([GetBook], f => f.File("out/haruspex.ir.json", $"{{ \"irVersion\": \"{IrDocument.CurrentVersion}\" }}"))),
        (6014, () => Checked([GetBook], f => f.File("db/p1.sql", GetBook.Replace("b.note, ", "", StringComparison.Ordinal)))),
        (6015, () => Checked([GetBook], f => File.AppendAllText(Path.Combine(f.Path, "out", "haruspex.zod.ts"), "// edited\n"))),
        (6016, () => Checked(["CREATE PROCEDURE lib.p @sql nvarchar(max) AS EXEC sp_executesql @sql;"], _ => { })),
        (6018, () => Cli([GetBook], args: ["--all", "db"])),
        (6019, () => Cli([GetBook], args: ["--all", "--out", "out"])),
        (6020, () => Cli([GetBook], args: ["-c", "a/haruspex.json", "-c", "b/haruspex.json"], arrange: f =>
        {
            f.File("a/haruspex.json", "{ \"source\": \"../db\", \"out\": \"../out\", \"schemas\": { \"lib\": { \"include\": [\"*\"] } } }");
            f.File("b/haruspex.json", "{ \"source\": \"../db\", \"out\": \"../out\", \"schemas\": { \"lib\": { \"include\": [\"*\"] } } }");
        })),
        (6017, () => Checked(["CREATE PROCEDURE lib.p AS SELECT json_query(b.extra) AS extra FROM lib.book AS b FOR JSON PATH;"], _ => { }, "\"check\": { \"unknowns\": \"fail\" }")),
    ];

    /// <summary>
    /// Where a message ends with the operating system's own words, which differ between Windows, macOS and Linux: the line is
    /// pinned up to there, and the rest shown as <c>…</c>.
    /// </summary>
    private static readonly Dictionary<int, string> SystemMessageAfter = new()
    {
        [3001] = "cannot read the schema schemas: ",
        [6008] = "cannot write to out: ",
    };

    /// <summary>The first line each case writes with its code, by code: what the snapshot and the pages show.</summary>
    internal static IReadOnlyDictionary<string, string> Lines { get; } = Cases.ToDictionary(
        c => $"HAR{c.Code:D4}",
        c => Pinned(c.Code, c.Run().FirstOrDefault(line => line.Contains($" HAR{c.Code:D4}: ", StringComparison.Ordinal)) ?? $"(no line with HAR{c.Code:D4}: {string.Join(" | ", c.Run())})"),
        StringComparer.Ordinal);

    private static string Pinned(int code, string line) =>
        SystemMessageAfter.TryGetValue(code, out var before) && line.IndexOf(before, StringComparison.Ordinal) is var at and >= 0
            ? line[..(at + before.Length)] + "…"
            : line;

    [Fact]
    public void Every_code_in_use_is_made_to_happen_by_a_case()
    {
        var declared = DiagnosticCodesTests.Declared(DiagnosticCodesTests.Layers()).Where(c => !c.Code.Retired).Select(c => c.Code.ToString()).Order(StringComparer.Ordinal);

        Assert.Equal(declared, Lines.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Each_case_writes_a_line_with_its_code() =>
        Assert.Empty(Lines.Where(l => l.Value.StartsWith("(no line", StringComparison.Ordinal)).Select(l => $"{l.Key}: {l.Value}"));

    [Fact]
    public void The_message_of_every_code_is_as_committed()
    {
        var text = new StringBuilder();
        foreach (var (code, line) in Lines.OrderBy(l => l.Key, StringComparer.Ordinal))
            text.Append(code).Append('\n').Append(line).Append("\n\n");

        Snapshot.Match(text.ToString(), "DiagnosticMessagesTests.Messages", "txt");
    }
}
