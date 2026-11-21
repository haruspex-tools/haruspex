using System.Globalization;
using System.Text;
using System.Text.Json;
using Haruspex.DifferentialTests.Server;

namespace Haruspex.DifferentialTests;

/// <summary>
/// How long a string is, as each party counts it: SQL Server's <c>LEN</c> and <c>DATALENGTH</c>, the length of the JavaScript string
/// the mssql driver hands over (UTF-16 code units), and its code points, which the pinned Zod's <c>.min</c>, <c>.max</c> and
/// <c>.length</c> count (<c>ZodObservationTests</c> in the unit tests). The emitters' lengths rest on these: a length in UTF-16
/// code units or bytes is a maximum of code points, and a fixed one a minimum only of the fewest code points it can hold.
/// </summary>
public partial class MssqlDriverObservationTests
{
    /// <summary>What a column of each type holds after the insert, its collation written out, so the database's default doesn't matter.</summary>
    private static readonly (string Name, string Type, string Inserted)[] Lengths =
    [
        ("nchar(4), two emoji", "nchar(4) COLLATE Latin1_General_CI_AS", "NCHAR(0xD83D) + NCHAR(0xDE00) + NCHAR(0xD83D) + NCHAR(0xDE00)"),
        ("nchar(4) _SC, two emoji", "nchar(4) COLLATE Latin1_General_100_CI_AS_SC", "NCHAR(0xD83D) + NCHAR(0xDE00) + NCHAR(0xD83D) + NCHAR(0xDE00)"),
        ("nchar(4), one emoji", "nchar(4) COLLATE Latin1_General_CI_AS", "NCHAR(0xD83D) + NCHAR(0xDE00)"),
        ("nchar(3), two emoji cut", "nchar(3) COLLATE Latin1_General_CI_AS", "CAST(NCHAR(0xD83D) + NCHAR(0xDE00) + NCHAR(0xD83D) + NCHAR(0xDE00) AS nchar(3))"),
        ("nvarchar(4), two combining accents", "nvarchar(4) COLLATE Latin1_General_CI_AS", "N'e' + NCHAR(0x0301) + N'e' + NCHAR(0x0301)"),
        ("char(8) UTF-8, two emoji", "char(8) COLLATE Latin1_General_100_CI_AS_SC_UTF8", "NCHAR(0xD83D) + NCHAR(0xDE00) + NCHAR(0xD83D) + NCHAR(0xDE00)"),
        ("char(8) UTF-8, an accent", "char(8) COLLATE Latin1_General_100_CI_AS_SC_UTF8", "NCHAR(0x00E9)"),
        ("varchar(8) UTF-8, a lone surrogate", "varchar(8) COLLATE Latin1_General_100_CI_AS_SC_UTF8", "NCHAR(0xD83D) + N'a'"),
        ("char(4) code page 1252", "char(4) COLLATE Latin1_General_CI_AS", "N'ab' + NCHAR(0x00E9)"),
        ("char(4) double-byte, two characters", "char(4) COLLATE Chinese_PRC_CI_AS", "NCHAR(0x4E2D) + NCHAR(0x6587)"),
        ("char(4) double-byte, one character", "char(4) COLLATE Chinese_PRC_CI_AS", "NCHAR(0x4E2D)"),

        // Longer than the column, inserted with ANSI_WARNINGS off, which cuts it short rather than failing.
        ("char(1) UTF-8, an accent cut", "char(1) COLLATE Latin1_General_100_CI_AS_SC_UTF8", "NCHAR(0x00E9)"),
        ("char(3) UTF-8, an emoji cut", "char(3) COLLATE Latin1_General_100_CI_AS_SC_UTF8", "N'a' + NCHAR(0xD83D) + NCHAR(0xDE00)"),
        ("char(3) double-byte, a character cut", "char(3) COLLATE Chinese_PRC_CI_AS", "N'a' + NCHAR(0x4E2D) + NCHAR(0x6587)"),
    ];

    [Fact]
    public async Task A_string_s_length_is_counted_by_the_server_in_code_units_or_bytes_and_by_zod_in_code_points()
    {
        // One row of a table with a column a case, each read as itself: a UNION would convert them to one type.
        var columns = string.Join(", ", Lengths.Select((c, i) => $"c{i} {c.Type}"));
        var values = string.Join(", ", Lengths.Select(c => c.Inserted));
        var measures = string.Join(", ", Lengths.Select((_, i) => $"LEN(c{i}) AS len{i}, DATALENGTH(c{i}) AS bytes{i}, c{i} AS v{i}"));
        var calls = await MssqlDriver.RunAsync(
            server,
            null,
            [$"CREATE TABLE dbo.obs_lengths ({columns});", $"SET ANSI_WARNINGS OFF; INSERT dbo.obs_lengths VALUES ({values}); SET ANSI_WARNINGS ON;"],
            [
                new("rowset", ParseJson: false, Batch: $"SELECT {measures} FROM dbo.obs_lengths;"),
                new("json", ParseJson: true, Batch: $"SELECT {string.Join(", ", Lengths.Select((_, i) => $"c{i} AS v{i}"))} FROM dbo.obs_lengths FOR JSON PATH;"),
            ],
            TestContext.Current.CancellationToken);

        var row = Assert.Single(OnlyRows(calls[0]));
        var observed = Lengths.Select((c, i) => (
            c.Name,
            Len: row.GetProperty($"len{i}").GetInt32(),
            Bytes: row.GetProperty($"bytes{i}").GetInt32(),
            Units: Text(row.GetProperty($"v{i}")).Length,
            CodePoints: CodePoints(Text(row.GetProperty($"v{i}")))));

        // Name: LEN, DATALENGTH, then the JavaScript string's length and its code points.
        Assert.Equal(
            [
                // UTF-16: an emoji is a surrogate pair, two code units and one code point. LEN counts code units, or code points under
                // a supplementary-character (_SC) collation. So nchar(4) holding two emoji is 4 code units but 2 code points, and Zod's
                // .length(4) refuses it.
                ("nchar(4), two emoji", 4, 8, 4, 2),
                ("nchar(4) _SC, two emoji", 2, 8, 4, 2),

                // Padded with spaces to 4 code units, which is 3 code points.
                ("nchar(4), one emoji", 2, 8, 4, 3),

                // Cut between the pair's halves: the high surrogate is kept alone, and the driver hands it over as one.
                ("nchar(3), two emoji cut", 3, 6, 3, 2),

                // A combining accent is a code point of its own, as Zod counts them (not a character as a reader sees it).
                ("nvarchar(4), two combining accents", 4, 8, 4, 4),

                // UTF-8: an emoji is 4 bytes, and a padded char(8) holding an accented letter (2 bytes) is that and 6 spaces.
                ("char(8) UTF-8, two emoji", 2, 8, 4, 2),
                ("char(8) UTF-8, an accent", 1, 8, 7, 7),

                // UTF-8 can't hold a lone surrogate: it becomes U+FFFD, 3 bytes, so 4 bytes with the letter.
                ("varchar(8) UTF-8, a lone surrogate", 2, 4, 2, 2),

                // A single-byte code page: a byte is a character, one code unit and one code point.
                ("char(4) code page 1252", 3, 4, 4, 4),

                // A double-byte code page: a character is one or two bytes, and always one code unit of the Basic Multilingual Plane.
                ("char(4) double-byte, two characters", 2, 4, 2, 2),
                ("char(4) double-byte, one character", 1, 4, 3, 3),

                // A char value cut short is cut between characters of its code page, never inside one, and padded: so a fixed length
                // of bytes holds at least as many code points as it has bytes divided by the most a character takes, and char(1)
                // exactly one.
                ("char(1) UTF-8, an accent cut", 0, 1, 1, 1),
                ("char(3) UTF-8, an emoji cut", 1, 3, 3, 3),
                ("char(3) double-byte, a character cut", 2, 3, 2, 2),
            ],
            observed);

        // A value converted to nchar(n) under a supplementary-character collation (the value's own, here, or the database's) is
        // cut before a pair that doesn't fit, and not padded: n - 1 code units, and nchar(1) of an emoji is empty. A stored
        // nchar(n) is always n (above). So a converted nchar(n) is at most n code units and at least n - 1 (SqlTypes.Converted), and a
        // stored one exactly n.
        var converted = await MssqlDriver.RunAsync(
            server,
            null,
            [],
            [new("converted", ParseJson: false, Batch: """
                DECLARE @e nvarchar(10) = NCHAR(0xD83D) + NCHAR(0xDE00);
                SELECT CAST((@e + @e) COLLATE Latin1_General_100_CI_AS_SC AS nchar(3)) AS v3,
                       CAST(@e COLLATE Latin1_General_100_CI_AS_SC AS nchar(1)) AS v1,
                       CAST((N'a' + @e + @e) COLLATE Latin1_General_100_CI_AS_SC AS nchar(4)) AS v4;
                """)],
            TestContext.Current.CancellationToken);
        var cut = Assert.Single(OnlyRows(converted[0]));
        Assert.Equal(
            [("nchar(3)", 2, 1), ("nchar(1)", 0, 0), ("nchar(4)", 3, 2)],
            new[] { ("nchar(3)", "v3"), ("nchar(1)", "v1"), ("nchar(4)", "v4") }.Select(c => (c.Item1, Text(cut.GetProperty(c.Item2)).Length, CodePoints(Text(cut.GetProperty(c.Item2))))));

        // FOR JSON writes each value as the rowset holds it, a lone surrogate included, and JSON.parse reads it back the same.
        var parsed = Assert.Single(Assert.Single(OnlyRows(calls[1])).EnumerateArray());
        Assert.Equal(Lengths.Select((_, i) => Text(row.GetProperty($"v{i}"))), Lengths.Select((_, i) => Text(parsed.GetProperty($"v{i}"))));
    }

    /// <summary>The rows of the one result set a call returned.</summary>
    private static IReadOnlyList<JsonElement> OnlyRows(MssqlDriver.Response response)
    {
        Assert.Null(response.Error);
        return Assert.Single(response.Recordsets).Rows;
    }

    /// <summary>
    /// A JSON string's text, its escapes decoded one code unit at a time: <c>JSON.stringify</c> writes a lone surrogate as an escape
    /// (<c>\ud83d</c>), which System.Text.Json refuses to decode into a string.
    /// </summary>
    private static string Text(JsonElement value)
    {
        var raw = value.GetRawText();
        var text = new StringBuilder();
        for (var i = 1; i < raw.Length - 1; i++)
        {
            if (raw[i] != '\\')
            {
                text.Append(raw[i]);
                continue;
            }

            var escape = raw[++i];
            if (escape == 'u')
            {
                text.Append((char)int.Parse(raw.AsSpan(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                i += 4;
            }
            else
            {
                text.Append(escape switch { 'b' => '\b', 'f' => '\f', 'n' => '\n', 'r' => '\r', 't' => '\t', _ => escape });
            }
        }

        return text.ToString();
    }

    /// <summary>Code points as JavaScript's string iterator counts them, which Zod uses: a pair is one, and so is a lone surrogate.</summary>
    private static int CodePoints(string text)
    {
        var count = 0;
        for (var i = 0; i < text.Length; i++, count++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                i++;
        }

        return count;
    }
}
