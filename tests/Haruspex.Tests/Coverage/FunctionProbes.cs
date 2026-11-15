using System.Collections.Frozen;

namespace Haruspex.Tests.Coverage;

/// <summary>
/// A call of each documented function the catalogue lacks, as a procedure body over <see cref="CoverageProbe.Schema"/>, so
/// that the coverage page says what haruspex gives for it by running it. A function is called as its form needs: an
/// <c>@@</c> function by its name, a window function with <c>OVER</c>, any other with one argument. The rest, written as
/// syntax of their own or read as a table, have their own call here.
/// </summary>
/// <remarks>
/// The argument count isn't checked: haruspex reads a call of a function it doesn't model no further than its name, and the
/// probe is never run on a server. What a probe says is what haruspex says of the function in that place.
/// </remarks>
internal static class FunctionProbes
{
    private static readonly FrozenDictionary<string, string> Own = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        // Keywords without parentheses, which ScriptDom reads as calls of their own.
        ["CURRENT_USER"] = "SELECT CURRENT_USER AS e;",
        ["SESSION_USER"] = "SELECT SESSION_USER AS e;",
        ["SYSTEM_USER"] = "SELECT SYSTEM_USER AS e;",
        ["USER"] = "SELECT USER AS e;",
        ["CURRENT_DATE"] = "SELECT CURRENT_DATE AS e;",

        // Syntax of their own.
        ["NEXT VALUE FOR"] = "SELECT NEXT VALUE FOR dbo.ticket AS e;",
        ["$PARTITION"] = "SELECT $PARTITION.by_id(id) AS e FROM dbo.toy;",
        ["IDENTITY"] = "SELECT IDENTITY(int, 1, 1) AS e INTO #t FROM dbo.toy; SELECT e FROM #t;",
        ["PARSE"] = "SELECT PARSE('1' AS int) AS e;",
        ["TRY_PARSE"] = "SELECT TRY_PARSE('1' AS int) AS e;",
        ["AI_GENERATE_EMBEDDINGS"] = "SELECT AI_GENERATE_EMBEDDINGS(name USE MODEL toy_model) AS e FROM dbo.toy;",
        ["APPROX_PERCENTILE_CONT"] = "SELECT APPROX_PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY id) AS e FROM dbo.toy;",
        ["APPROX_PERCENTILE_DISC"] = "SELECT APPROX_PERCENTILE_DISC(0.5) WITHIN GROUP (ORDER BY id) AS e FROM dbo.toy;",

        // Conditions, not values.
        ["REGEXP_LIKE"] = "SELECT id AS e FROM dbo.toy WHERE REGEXP_LIKE(name, '^b');",
        ["UPDATE"] = "IF UPDATE(qty) SELECT 1 AS e;",

        // Read as tables.
        ["STRING_SPLIT"] = "SELECT value AS e FROM STRING_SPLIT('a,b', ',');",
        ["REGEXP_MATCHES"] = "SELECT m.match_value AS e FROM REGEXP_MATCHES('ab', 'a') AS m;",
        ["REGEXP_SPLIT_TO_TABLE"] = "SELECT s.value AS e FROM REGEXP_SPLIT_TO_TABLE('a,b', ',') AS s;",
        ["OPENDATASOURCE"] = "SELECT a.id AS e FROM OPENDATASOURCE('MSOLEDBSQL', 'Server=elsewhere;').shop.dbo.toy AS a;",
        ["OPENQUERY"] = "SELECT q.a AS e FROM OPENQUERY(elsewhere, 'SELECT 1 AS a') AS q;",
        ["OPENROWSET"] = "SELECT r.a AS e FROM OPENROWSET('MSOLEDBSQL', 'Server=elsewhere;', 'SELECT 1 AS a') AS r;",
        ["OPENXML"] = "DECLARE @h int; SELECT x.a AS e FROM OPENXML(@h, '/r') WITH (a int) AS x;",
        ["PREDICT"] = "DECLARE @m varbinary(max); SELECT p.score AS e FROM PREDICT(MODEL = @m, DATA = dbo.toy AS d) WITH (score float) AS p;",
        ["AI_GENERATE_CHUNKS"] = "SELECT c.chunk AS e FROM AI_GENERATE_CHUNKS(SOURCE = 'some text', CHUNK_TYPE = FIXED, CHUNK_SIZE = 5) AS c;",
        ["VECTOR_SEARCH"] = "DECLARE @v vector(3) = '[1,2,3]'; SELECT s.distance AS e FROM VECTOR_SEARCH(TABLE = dbo.toy AS t, COLUMN = place, SIMILAR_TO = @v, METRIC = 'cosine', TOP_N = 1) AS s;",
        ["sys.fn_builtin_permissions"] = "SELECT p.permission_name AS e FROM sys.fn_builtin_permissions(DEFAULT) AS p;",
        ["sys.fn_get_audit_file"] = "SELECT a.event_time AS e FROM sys.fn_get_audit_file('audit.sqlaudit', DEFAULT, DEFAULT) AS a;",
        ["sys.fn_my_permissions"] = "SELECT p.permission_name AS e FROM sys.fn_my_permissions(NULL, 'DATABASE') AS p;",
        ["fn_virtualfilestats"] = "SELECT f.NumberReads AS e FROM fn_virtualfilestats(NULL, NULL) AS f;",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>The window functions' categories, whose functions are called with <c>OVER</c>.</summary>
    private static readonly string[] Windowed = ["Analytic functions", "Ranking functions"];

    /// <summary>The body that calls a documented function of a category.</summary>
    public static string For(string name, string category) =>
        Own.TryGetValue(name, out var body) ? body
        : name.StartsWith("@@", StringComparison.Ordinal) ? $"SELECT {name} AS e;"
        : Windowed.Contains(category, StringComparer.Ordinal) ? $"SELECT {name}() OVER (ORDER BY id) AS e FROM dbo.toy;"
        : $"SELECT {name}(id) AS e FROM dbo.toy;";
}
