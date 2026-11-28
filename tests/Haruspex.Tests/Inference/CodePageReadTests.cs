using System.Text;
using Haruspex.Cli;
using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

/// <summary>
/// JSON read from a declared <c>varchar</c> or <c>char</c> column or parameter keeps a value set or length only where it holds after
/// the text's code page: a character the code page lacks, written as itself, comes back as another (a best fit, or <c>?</c>, and a
/// character outside the Basic Multilingual Plane as <c>??</c>). The same cases run for a column and a parameter, which share the
/// rule (<c>CodePageText</c>). Why: docs/decisions/0145-varchar-json-is-read-through-its-code-page.md; observed in
/// <c>CodePageObservationTests</c>.
/// </summary>
public class CodePageReadTests
{
    // Ω: code pages 1252 and 1251 lack it (1252 gives O, 1251 ?), 932 and UTF-8 hold it. é: 1252 holds it, 1251 gives e.
    // Ж: 1251 holds it, 1252 gives ?. 😀: no code page but UTF-8 holds it; it comes back as ??, two code points.
    private const string Badge = """
        { "type": "object", "required": ["tier", "mode", "place", "letter", "face", "name", "quoted"], "properties": {
            "tier": { "enum": ["Ω", "alpha"] },
            "mode": { "enum": ["fast", "slow"] },
            "place": { "enum": ["café", "bar"] },
            "letter": { "enum": ["Ж", "z"] },
            "face": { "enum": ["😀"] },
            "name": { "type": "string", "minLength": 2, "maxLength": 3 },
            "quoted": { "enum": ["a\"b", "tab\tstop"] } } }
        """;

    private static readonly string[] Keys = ["tier", "mode", "place", "letter", "face", "name", "quoted"];

    /// <summary>The reads of each key of <paramref name="text"/>'s JSON.</summary>
    private static string Reads(string text) => string.Join(", ", Keys.Select(k => $"json_value({text}, '$.{k}') AS {k}"));

    /// <summary>
    /// The declared JSON's facts, as <c>json_value</c> reads them, of a column or a parameter of <paramref name="type"/> in a
    /// database whose default collation is <paramref name="database"/>.
    /// </summary>
    private static IrDocument Read(bool column, string type, string? database)
    {
        using var dir = new TempDirectory();
        var reads = Reads(column ? "b.body" : "@body");
        dir.File("db/objects.sql", column
            ? $"""
                CREATE SCHEMA shop;
                GO
                CREATE TABLE shop.badges (code varchar(40) NOT NULL, body {type} NOT NULL);
                GO
                CREATE PROCEDURE shop.p AS SELECT {reads} FROM shop.badges AS b;
                """
            : $"""
                CREATE SCHEMA shop;
                GO
                CREATE PROCEDURE shop.p @body {type} AS IF @body IS NULL RETURN; SELECT {reads};
                """);
        dir.File("schemas/badge.schema.json", Badge);
        var project = SqlProject.Load(Path.Combine(dir.Path, "db"), database);
        Assert.DoesNotContain(project.Diagnostics, d => d.IsError);
        var setting = column ? """ "columns": { "shop.badges.body": { "json": "schemas/badge.schema.json" } } """ : """ "parameters": { "shop.p.@body": { "json": "schemas/badge.schema.json" } } """;
        var read = ConfigReader.Read(Encoding.UTF8.GetBytes($"{{ {setting} }}"), Path.Combine(dir.Path, "haruspex.json"), dir.Path);
        Assert.DoesNotContain(read.Diagnostics, d => d.IsError);
        return project.Infer(project.Procedures, read.Expected()).Expected();
    }

    private static string[] Values(IrDocument doc, string name) =>
        [.. ((Field(doc, name).Type as IrScalar)?.Facets?.Values?.Items ?? []).Select(v => v.Text).Order(StringComparer.Ordinal)];

    private static IrFacets? Facets(IrDocument doc, string name) => Assert.IsType<IrScalar>(Field(doc, name).Type).Facets;

    public static TheoryData<bool> ColumnOrParameter => [true, false];

    [Theory]
    [MemberData(nameof(ColumnOrParameter))]
    public void Under_code_page_1252_a_value_set_is_kept_only_when_1252_holds_every_character(bool column)
    {
        var doc = Read(column, "varchar(max)", "SQL_Latin1_General_CP1_CI_AS");

        Assert.Empty(Values(doc, "tier"));
        Assert.Equal(["fast", "slow"], Values(doc, "mode"));
        Assert.Equal(["bar", "café"], Values(doc, "place"));
        Assert.Empty(Values(doc, "letter"));
        Assert.Empty(Values(doc, "face"));
        Assert.Equal(["a\"b", "tab\tstop"], Values(doc, "quoted"));
    }

    [Theory]
    [MemberData(nameof(ColumnOrParameter))]
    public void A_char_column_or_parameter_is_read_through_its_code_page_as_a_varchar_one_is(bool column)
    {
        var doc = Read(column, "char(200)", "SQL_Latin1_General_CP1_CI_AS");

        Assert.Empty(Values(doc, "tier"));
        Assert.Equal(["bar", "café"], Values(doc, "place"));
        Assert.Empty(Values(doc, "face"));
    }

    [Theory]
    [MemberData(nameof(ColumnOrParameter))]
    public void Under_code_page_1251_another_code_page_s_characters_are_kept(bool column)
    {
        var doc = Read(column, "varchar(max)", "Cyrillic_General_CI_AS");

        Assert.Empty(Values(doc, "tier"));
        Assert.Empty(Values(doc, "place"));
        Assert.Equal(["z", "Ж"], Values(doc, "letter"));
        Assert.Equal(["fast", "slow"], Values(doc, "mode"));
    }

    [Theory]
    [MemberData(nameof(ColumnOrParameter))]
    public void A_column_s_own_collation_decides_and_not_the_database_s(bool column)
    {
        // A parameter has no COLLATE: it takes the database's, so only the column changes here.
        var doc = Read(column, column ? "varchar(max) COLLATE Cyrillic_General_CI_AS" : "varchar(max)", "SQL_Latin1_General_CP1_CI_AS");

        Assert.Equal(column ? ["z", "Ж"] : [], Values(doc, "letter"));
        Assert.Equal(column ? [] : ["bar", "café"], Values(doc, "place"));
    }

    [Theory]
    [MemberData(nameof(ColumnOrParameter))]
    public void Where_the_collation_isn_t_known_only_ascii_values_are_kept(bool column)
    {
        var doc = Read(column, "varchar(max)", null);

        Assert.Empty(Values(doc, "tier"));
        Assert.Empty(Values(doc, "place"));
        Assert.Empty(Values(doc, "letter"));
        Assert.Equal(["fast", "slow"], Values(doc, "mode"));
        Assert.Equal(["a\"b", "tab\tstop"], Values(doc, "quoted"));
    }

    [Theory]
    [MemberData(nameof(ColumnOrParameter))]
    public void A_code_page_keeps_a_least_length_and_doubles_a_greatest_since_one_character_may_come_back_as_two(bool column)
    {
        // 😀, one code point, comes back as ??, two: a maxLength of 3 code points becomes 6. Every UTF-16 unit comes back as one
        // character, so a minLength holds, and no value has more than twice as many.
        var name = Facets(Read(column, "varchar(max)", "SQL_Latin1_General_CP1_CI_AS"), "name");

        Assert.Equal(2, name?.MinLength?.Value);
        Assert.Equal((6, IrLengthUnit.CodePoints), (name?.Length?.Value, name?.Length?.Unit));
    }

    [Theory]
    [MemberData(nameof(ColumnOrParameter))]
    public void Under_utf_8_every_character_is_kept_with_its_lengths(bool column)
    {
        var doc = Read(column, column ? "varchar(max) COLLATE Latin1_General_100_CI_AS_SC_UTF8" : "varchar(max)", column ? null : "Latin1_General_100_CI_AS_SC_UTF8");

        Assert.Equal(["alpha", "Ω"], Values(doc, "tier"));
        Assert.Equal(["😀"], Values(doc, "face"));
        Assert.Equal(["z", "Ж"], Values(doc, "letter"));
        Assert.Equal((3, IrLengthUnit.CodePoints), (Facets(doc, "name")?.Length?.Value, Facets(doc, "name")?.Length?.Unit));
    }

    [Theory]
    [MemberData(nameof(ColumnOrParameter))]
    public void Nvarchar_text_keeps_everything_declared(bool column)
    {
        var doc = Read(column, "nvarchar(max)", null);

        Assert.Equal(["alpha", "Ω"], Values(doc, "tier"));
        Assert.Equal(["😀"], Values(doc, "face"));
        Assert.Equal((3, IrLengthUnit.CodePoints), (Facets(doc, "name")?.Length?.Value, Facets(doc, "name")?.Length?.Unit));
    }

    [Fact]
    public void Openjson_with_over_a_declared_varchar_column_reads_what_the_code_page_gives()
    {
        using var dir = new TempDirectory();
        dir.File("db/objects.sql", """
            CREATE SCHEMA shop;
            GO
            CREATE TABLE shop.badges (code varchar(40) NOT NULL, body varchar(max) NOT NULL);
            GO
            CREATE PROCEDURE shop.p AS SELECT w.tier, w.mode FROM shop.badges AS b CROSS APPLY OPENJSON(b.body) WITH (tier nvarchar(20) '$.tier', mode nvarchar(20) '$.mode') AS w;
            """);
        dir.File("schemas/badge.schema.json", Badge);
        var project = SqlProject.Load(Path.Combine(dir.Path, "db"), "SQL_Latin1_General_CP1_CI_AS");
        var read = ConfigReader.Read(Encoding.UTF8.GetBytes("""{ "columns": { "shop.badges.body": { "json": "schemas/badge.schema.json" } } }"""), Path.Combine(dir.Path, "haruspex.json"), dir.Path);
        var doc = project.Infer(project.Procedures, read.Expected()).Expected();

        Assert.Empty(Values(doc, "tier"));
        Assert.Equal(["fast", "slow"], Values(doc, "mode"));
    }

    // ---- keys -------------------------------------------------------------------------------------------------------

    private const string Keyed = """
        { "type": "object", "additionalProperties": false, "required": ["Ω", "O", "long"], "properties": {
            "Ω": { "enum": ["x"] }, "O": { "type": "integer" }, "long": { "type": "integer" } } }
        """;

    /// <summary>The object type a plain read of the declared column or parameter holds, in a database of <paramref name="collation"/>.</summary>
    private static IrObjectType KeyedType(bool column, string collation)
    {
        using var dir = new TempDirectory();
        dir.File("db/objects.sql", column
            ? """
                CREATE SCHEMA shop;
                GO
                CREATE TABLE shop.badges (code varchar(40) NOT NULL, body varchar(max) NOT NULL);
                GO
                CREATE PROCEDURE shop.p AS SELECT b.body FROM shop.badges AS b;
                """
            : """
                CREATE SCHEMA shop;
                GO
                CREATE PROCEDURE shop.p @body varchar(max) AS IF @body IS NULL RETURN; SELECT @body AS body;
                """);
        dir.File("schemas/keyed.schema.json", Keyed);
        var project = SqlProject.Load(Path.Combine(dir.Path, "db"), collation);
        var setting = column ? """ "columns": { "shop.badges.body": { "json": "schemas/keyed.schema.json" } } """ : """ "parameters": { "shop.p.@body": { "json": "schemas/keyed.schema.json" } } """;
        var read = ConfigReader.Read(Encoding.UTF8.GetBytes($"{{ {setting} }}"), Path.Combine(dir.Path, "haruspex.json"), dir.Path);
        var doc = project.Infer(project.Procedures, read.Expected()).Expected();
        var content = Assert.IsType<IrRef>(Assert.IsType<IrJsonText>(Field(doc, "body").Type).Content);
        return doc.Types[content.Name];
    }

    [Theory]
    [MemberData(nameof(ColumnOrParameter))]
    public void A_key_the_code_page_lacks_may_be_missing_leaves_the_object_open_and_may_be_written_over_a_key_of_its_length(bool column)
    {
        // Under 1252, {"Ω":"x","O":1} is stored as {"O":"x","O":1}: "Ω" is never found by a path, "O" may be read as "x", and a
        // plain read holds a key the schema doesn't list. A key of another length ("long") can't be one Ω becomes.
        var type = KeyedType(column, "SQL_Latin1_General_CP1_CI_AS");
        var field = type.Fields.ToDictionary(f => f.Name);

        Assert.True(type.Open);
        Assert.Equal(IrPresence.Optional, field["Ω"].Presence);
        Assert.Equal(2, Assert.IsType<IrUnion>(field["O"].Type).Variants.Count);
        Assert.Equal(IrPresence.Always, field["O"].Presence);
        Assert.IsType<IrScalar>(field["long"].Type);
    }

    [Theory]
    [MemberData(nameof(ColumnOrParameter))]
    public void A_key_the_code_page_holds_is_read_as_declared(bool column)
    {
        // Code page 1253 (Greek) holds Ω (observed by ann3-review: under Greek_CI_AS, '$."Ω"' finds it).
        var type = KeyedType(column, "Greek_CI_AS");

        Assert.False(type.Open);
        Assert.All(type.Fields, f => Assert.Equal(IrPresence.Always, f.Presence));
        Assert.IsType<IrScalar>(type.Fields.Single(f => f.Name == "O").Type);
    }
}
