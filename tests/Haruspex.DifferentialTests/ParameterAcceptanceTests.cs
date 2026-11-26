using System.Globalization;
using Haruspex.DifferentialTests.Server;
using Haruspex.Emit.Zod;

namespace Haruspex.DifferentialTests;

/// <summary>
/// Holds the generated parameter schemas to the server, value by value: each value is parsed with the schema haruspex writes for
/// a parameter of its type, and sent to that parameter through the mssql driver, bound as the generated functions bind it. A
/// value the schema accepts must arrive as it was sent (decision 0054); one it refuses must be one the server changes or
/// refuses, or one written in a form other than the one the schema asks for, which the case says.
/// </summary>
public class ParameterAcceptanceTests(SqlServerFixture server)
{
    /// <summary>What a case shows.</summary>
    private enum Expected
    {
        /// <summary>The schema accepts it, and the server keeps it as it was sent.</summary>
        Kept,

        /// <summary>The schema refuses it, and the server would change it or refuse it.</summary>
        Changed,

        /// <summary>The schema refuses it for its form, though the server would keep it: the schema asks for one spelling.</summary>
        Form,
    }

    /// <summary>One value sent to a parameter of one type, bound as the generated function binds it.</summary>
    private sealed record Case(string Declared, MssqlDriver.Typed Sent, Expected Expected);

    private static Case Of(string declared, string type, object? value, Expected expected, params object[] args) =>
        new(declared, new MssqlDriver.Typed(type, value, args.Length == 0 ? null : args), expected);

    private static readonly Case[] Cases =
    [
        Of("nvarchar(5)", "NVarChar", "abcde", Expected.Kept, 5),
        Of("nvarchar(5)", "NVarChar", "abc\U0001F600", Expected.Kept, 5),
        Of("nvarchar(5)", "NVarChar", "abcdef", Expected.Changed, 5),
        Of("nvarchar(5)", "NVarChar", "abcd\U0001F600", Expected.Changed, 5),
        Of("nchar(5)", "NChar", "ab", Expected.Kept, 5),
        Of("nchar(5)", "NChar", "abcdef", Expected.Changed, 5),
        Of("varchar(5)", "VarChar", "abcde", Expected.Kept, 5),
        Of("varchar(5)", "VarChar", "abcdef", Expected.Changed, 5),
        Of("nvarchar(max)", "NVarChar", new string('x', 9000), Expected.Kept, "MAX"),

        Of("decimal(5,2)", "Decimal", 999.99, Expected.Kept, 5, 2),
        Of("decimal(5,2)", "Decimal", -999.99, Expected.Kept, 5, 2),
        Of("decimal(5,2)", "Decimal", 0.07, Expected.Kept, 5, 2),
        Of("decimal(5,2)", "Decimal", 1.234, Expected.Changed, 5, 2),
        Of("decimal(5,2)", "Decimal", 999.995, Expected.Changed, 5, 2),
        Of("decimal(5,2)", "Decimal", 1000, Expected.Changed, 5, 2),
        Of("money", "Decimal", 1.2345, Expected.Kept, 19, 4),
        Of("money", "Decimal", 1.23456, Expected.Changed, 19, 4),
        Of("smallmoney", "Decimal", 214748.3647, Expected.Kept, 10, 4),
        Of("smallmoney", "Decimal", 214748.3648, Expected.Changed, 10, 4),
        Of("tinyint", "TinyInt", 255, Expected.Kept),
        Of("tinyint", "TinyInt", 256, Expected.Changed),
        Of("tinyint", "TinyInt", -1, Expected.Changed),
        Of("smallint", "SmallInt", -32768, Expected.Kept),
        Of("smallint", "SmallInt", 32768, Expected.Changed),
        Of("int", "Int", 2147483647, Expected.Kept),
        Of("int", "Int", 1.5, Expected.Changed),
        Of("int", "Int", 2147483648.0, Expected.Changed),
        Of("bigint", "BigInt", 9007199254740991, Expected.Kept),
        Of("bigint", "BigInt", new MssqlDriver.BigIntValue("9223372036854775807"), Expected.Kept),
        Of("bigint", "BigInt", new MssqlDriver.BigIntValue("-9223372036854775808"), Expected.Kept),
        Of("bigint", "BigInt", new MssqlDriver.BigIntValue("9223372036854775808"), Expected.Changed),
        Of("bigint", "BigInt", "9007199254740993", Expected.Kept),
        Of("bigint", "BigInt", "-9223372036854775808", Expected.Kept),
        Of("bigint", "BigInt", "9223372036854775808", Expected.Changed),

        // Refused, not thrown: the range's check reads the digits only once the string has them.
        Of("bigint", "BigInt", "1.5", Expected.Changed),
        Of("bigint", "BigInt", "1e3", Expected.Changed),
        Of("float", "Float", 1e300, Expected.Kept),
        Of("float", "Float", new MssqlDriver.JsValue("NaN"), Expected.Changed),
        Of("bit", "Bit", true, Expected.Kept),
        Of("bit", "Bit", 2, Expected.Changed),
        Of("uniqueidentifier", "UniqueIdentifier", "0f8fad5b-d9cb-469f-a165-70867728950e", Expected.Kept),
        Of("uniqueidentifier", "UniqueIdentifier", "{0f8fad5b-d9cb-469f-a165-70867728950e}", Expected.Changed),

        Of("date", "VarChar", "2026-11-16", Expected.Kept),
        Of("date", "VarChar", "0001-01-01", Expected.Kept),
        Of("date", "VarChar", "2026-02-30", Expected.Changed),
        Of("time(3)", "VarChar", "08:30:00.125", Expected.Kept),
        Of("time(3)", "VarChar", "08:30:00.1255", Expected.Changed),
        Of("time(3)", "VarChar", "08:30", Expected.Form),
        Of("datetime2(0)", "VarChar", "2026-11-16T08:30:00", Expected.Kept),
        Of("datetime2(0)", "VarChar", "2026-11-16T08:30:00.5", Expected.Changed),
        Of("datetime2(0)", "VarChar", "2026-11-16T08:30:00Z", Expected.Changed),
        Of("datetime2(7)", "VarChar", "2026-11-16T08:30:00.1234567", Expected.Kept),
        Of("datetimeoffset(7)", "VarChar", "2026-11-16T08:30:00.1234567+05:30", Expected.Kept),
        Of("datetimeoffset(7)", "VarChar", "2026-11-16T08:30:00Z", Expected.Kept),
        Of("datetimeoffset(7)", "VarChar", "2026-11-16T08:30:00", Expected.Changed),
        Of("datetime", "VarChar", "2026-11-16T08:30:00.997", Expected.Kept),
        Of("datetime", "VarChar", "1753-01-01T00:00:00", Expected.Kept),
        Of("datetime", "VarChar", "9999-12-31T23:59:59.997", Expected.Kept),
        Of("datetime", "VarChar", "2026-11-16T08:30:00.998", Expected.Changed),
        Of("datetime", "VarChar", "1752-12-31T23:59:59", Expected.Changed),
        Of("datetime", "VarChar", "2026-11-16", Expected.Form),
        Of("smalldatetime", "VarChar", "2026-11-16T08:30:00", Expected.Kept),
        Of("smalldatetime", "VarChar", "2079-06-06T23:59:00", Expected.Kept),
        Of("smalldatetime", "VarChar", "2026-11-16T08:30:30", Expected.Changed),
        Of("smalldatetime", "VarChar", "2079-06-07T00:00:00", Expected.Changed),
        Of("smalldatetime", "VarChar", "1899-12-31T23:59:00", Expected.Changed),
    ];

    /// <summary>The name of the procedure that echoes a parameter of <paramref name="declared"/>: <c>shop.accept_datetime2_7</c>.</summary>
    private static string ProcedureOf(string declared) =>
        "shop.accept_" + new string([.. declared.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_')]).TrimEnd('_').Replace("__", "_", StringComparison.Ordinal);

    /// <summary>
    /// A procedure that returns what <c>@v</c> holds as text: a date or time in ISO 8601 (style 126), money with its four places
    /// (style 2), a float with all its digits (style 3), anything else as <c>CONVERT</c> writes it.
    /// </summary>
    private static string Echo(string declared)
    {
        var style = declared switch
        {
            _ when declared.StartsWith("date", StringComparison.Ordinal) || declared.StartsWith("time", StringComparison.Ordinal) || declared.EndsWith("datetime", StringComparison.Ordinal) => ", 126",
            "money" or "smallmoney" => ", 2",
            "float" => ", 3",
            _ => "",
        };
        return $"CREATE PROCEDURE {ProcedureOf(declared)} @v {declared} AS SELECT CONVERT(nvarchar(max), @v{style}) AS v;";
    }

    [Fact]
    public async Task A_value_the_parameter_schema_accepts_arrives_as_it_was_sent()
    {
        var declared = Cases.Select(c => c.Declared).Distinct().ToList();
        var document = InferProcedure.Infer("shop.*", [("schema.sql", "CREATE SCHEMA shop;"), .. declared.Select(d => ($"{ProcedureOf(d)}.sql", Echo(d)))]);
        var module = ZodEmitter.Emit(document).Expected();
        var calls = Cases.Select((c, i) => new MssqlDriver.Call(
            $"{i}: {c.Declared} {c.Sent.Value}",
            ParseJson: false,
            Procedure: ProcedureOf(c.Declared),
            Parameters: new Dictionary<string, object?> { ["v"] = c.Sent },
            Accepts: document.Objects.Single(o => o.Id == ProcedureOf(c.Declared)).Name + "ParamsSchema")).ToList();

        // One run a case: a value longer than the length it is sent with breaks the stream, which aborts the run's transaction.
        string[] setup = ["IF SCHEMA_ID(N'shop') IS NULL EXEC (N'CREATE SCHEMA shop');", .. declared.Select(Echo)];
        var responses = new List<MssqlDriver.Response>();
        foreach (var call in calls)
            responses.Add(Assert.Single(await MssqlDriver.RunAsync(server, module, setup, [call], TestContext.Current.CancellationToken)));

        var wrong = Cases.Zip(responses).Select(pair =>
        {
            var (c, response) = (pair.First, pair.Second);
            var kept = response.Error is null && Kept(c.Declared, c.Sent.Value, Assert.Single(Assert.Single(response.Recordsets).Rows).GetProperty("v").GetString());
            var shown = c.Expected switch
            {
                Expected.Kept => response.Accepted == true && kept,
                Expected.Changed => response.Accepted == false && !kept,
                Expected.Form => response.Accepted == false && kept,
                _ => false,
            };
            return shown ? null : $"{response.Name}: expected {c.Expected}, the schema {(response.Accepted == true ? "accepted" : "refused")} it and the server {(kept ? "kept it" : response.Error is { } e ? $"refused it ({e.Code} {e.Number})" : "changed it")}";
        }).OfType<string>().ToList();
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    [Fact]
    public async Task Xml_the_parameter_schema_accepts_may_still_fail_the_call_with_the_server_s_error_9400()
    {
        // The schema takes any string, since checking XML needs a parser (decision 0133): text that isn't XML passes it and the
        // server refuses the call when it parses it, error 9400, at run time; haruspex claims nothing it would prevent. Well-formed
        // XML arrives as the server writes it.
        const string Echo = "CREATE PROCEDURE shop.echo_xml @v xml AS SELECT CONVERT(nvarchar(max), @v) AS v;";
        var document = InferProcedure.Infer("shop.*", ("schema.sql", "CREATE SCHEMA shop;"), ("echo.sql", Echo));
        var module = ZodEmitter.Emit(document).Expected();
        string[] setup = ["IF SCHEMA_ID(N'shop') IS NULL EXEC (N'CREATE SCHEMA shop');", Echo];
        static MssqlDriver.Call Call(string text) => new(text, ParseJson: false, Procedure: "shop.echo_xml", Parameters: new Dictionary<string, object?> { ["v"] = new MssqlDriver.Typed("Xml", text) }, Accepts: "EchoXmlParamsSchema");

        // One run a call: the error ends the transaction the calls of a run share.
        var malformed = Assert.Single(await MssqlDriver.RunAsync(server, module, setup, [Call("<a")], TestContext.Current.CancellationToken));
        var wellFormed = Assert.Single(await MssqlDriver.RunAsync(server, module, setup, [Call("<a  b='1'/>")], TestContext.Current.CancellationToken));

        Assert.Equal((true, 9400), (malformed.Accepted, malformed.Error?.Number));
        Assert.True(wellFormed.Accepted);
        Assert.Equal("<a b=\"1\"/>", Assert.Single(Assert.Single(wellFormed.Recordsets).Rows).GetProperty("v").GetString());
    }

    /// <summary>Whether the server's text for the value it holds is the value sent, compared as the type's values compare.</summary>
    private static bool Kept(string declared, object? sent, string? kept)
    {
        if (kept is null)
            return sent is null;
        var text = sent switch
        {
            MssqlDriver.BigIntValue big => big.BigInt,
            MssqlDriver.JsValue => null,
            bool b => b ? "1" : "0",
            IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
            _ => sent?.ToString(),
        };
        if (text is null)
            return false;
        var type = declared.Split('(')[0];
        return type switch
        {
            "nchar" or "char" => kept.TrimEnd(' ') == text.TrimEnd(' '),
            "decimal" or "money" or "smallmoney" or "tinyint" or "smallint" or "int" or "bigint" =>
                decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && decimal.Parse(kept, NumberStyles.Float, CultureInfo.InvariantCulture) == d,
            "float" => double.Parse(kept, CultureInfo.InvariantCulture) == double.Parse(text, CultureInfo.InvariantCulture),
            "uniqueidentifier" => Guid.TryParse(text, out var g) && Guid.Parse(kept, CultureInfo.InvariantCulture) == g,
            "date" => DateOnly.TryParse(text, CultureInfo.InvariantCulture, out var day) && DateOnly.Parse(kept, CultureInfo.InvariantCulture) == day,
            "time" => TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var time) && TimeSpan.Parse(kept, CultureInfo.InvariantCulture) == time,

            // A value with an offset sent to a type without one has lost it, and one without an offset sent to a type with one
            // has been given one.
            "datetimeoffset" => HasOffset(text) && DateTimeOffset.Parse(text, CultureInfo.InvariantCulture) is var at
                && DateTimeOffset.Parse(kept, CultureInfo.InvariantCulture) is var held && held == at && held.Offset == at.Offset,
            "datetime2" or "datetime" or "smalldatetime" => !HasOffset(text)
                && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)
                && DateTime.Parse(kept, CultureInfo.InvariantCulture, DateTimeStyles.None) == local,
            _ => kept == text,
        };
    }

    private static bool HasOffset(string text) =>
        text.Length > 19 && (text.EndsWith('Z') || text[^6] is '+' or '-');
}
