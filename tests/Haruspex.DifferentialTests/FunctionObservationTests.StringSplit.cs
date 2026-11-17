namespace Haruspex.DifferentialTests;

// STRING_SPLIT as a table source: its columns' types, when there are rows, and the compatibility levels that have it.
// Inference's README.md, "STRING_SPLIT", gives the rules these rest on.
public partial class FunctionObservationTests
{
    [Fact]
    public async Task String_split_gives_value_of_its_strings_type_varying_never_null_and_ordinal_only_when_asked()
    {
        // value is the string's type made varying, of its length, max staying max, nvarchar if the separator is.
        Assert.Equal("value varchar(10)", await Describe($"SELECT v.value FROM {Row} CROSS APPLY STRING_SPLIT(x.s, ',') AS v"));
        Assert.Equal("value varchar(5)", await Describe($"SELECT v.value FROM {Row} CROSS APPLY STRING_SPLIT(x.c, ',') AS v"));
        Assert.Equal("value nvarchar(4)", await Describe($"SELECT v.value FROM {Row} CROSS APPLY STRING_SPLIT(x.nc, N',') AS v"));
        Assert.Equal("value varchar(max)", await Describe($"SELECT v.value FROM {Row} CROSS APPLY STRING_SPLIT(x.vm, ',') AS v"));
        Assert.Equal("value nvarchar(max)", await Describe($"SELECT v.value FROM {Row} CROSS APPLY STRING_SPLIT(x.nm, N',') AS v"));
        Assert.Equal("value nvarchar(10)", await Describe($"SELECT v.value FROM {Row} CROSS APPLY STRING_SPLIT(x.s, N',') AS v"));
        Assert.Equal("value nvarchar(3)", await Describe("SELECT value FROM STRING_SPLIT(N'a,b', N',')"));

        // ordinal, a bigint, only when the third argument is the literal 1; 0 and NULL leave it out.
        Assert.Equal("value varchar(10), ordinal bigint", await Describe($"SELECT v.* FROM {Row} CROSS APPLY STRING_SPLIT(x.s, ',', 1) AS v"));
        Assert.Equal("value varchar(10)", await Describe($"SELECT v.* FROM {Row} CROSS APPLY STRING_SPLIT(x.s, ',', 0) AS v"));
        Assert.Equal("value varchar(10)", await Describe($"SELECT v.* FROM {Row} CROSS APPLY STRING_SPLIT(x.s, ',', NULL) AS v"));

        // An empty piece is '', never NULL; ordinals count from 1; a NULL string gives no rows.
        Assert.Equal(
            "value,ordinal: a,1;,2;b,3;,4 | n: 0",
            await Select("""
                DECLARE @s nvarchar(10) = NULL;
                SELECT value, ordinal FROM STRING_SPLIT(N'a,,b,', N',', 1) ORDER BY ordinal;
                SELECT COUNT(*) AS n FROM STRING_SPLIT(@s, N',');
                """));

        // A NULL separator or one longer than a character is an error (214); so is an ordinal flag that isn't a literal (8748)
        // or is a literal other than 0 or 1 (4199); a string that isn't a string is refused (8116).
        Assert.Equal(" ! 214", (await Run([], "DECLARE @p nvarchar(1) = NULL; SELECT value FROM STRING_SPLIT(N'a', @p);")).ToString());
        Assert.Equal(" ! 214", (await Run([], "SELECT value FROM STRING_SPLIT(N'a', N',,');")).ToString());
        Assert.Equal(8748, (await Run([], "DECLARE @o int = 1; SELECT value FROM STRING_SPLIT(N'a', N',', @o);")).Error);
        Assert.Equal(4199, (await Run([], "SELECT value FROM STRING_SPLIT(N'a', N',', 2);")).Error);
        Assert.Equal(8116, (await Run([], $"SELECT v.value FROM {Row} CROSS APPLY STRING_SPLIT(x.i, ',') AS v;")).Error);
    }

    [Fact]
    public async Task String_split_needs_compatibility_level_130_and_has_its_ordinal_from_there()
    {
        // Below 130 SQL Server doesn't find the name (208), in a query and when a procedure that reads it runs; from 130 it is
        // there, ordinal too. A test can't change the shared database's level inside its transaction, so this uses its own.
        var token = TestContext.Current.CancellationToken;
        await using var scratch = await server.ScratchAsync();
        var outcomes = new List<string>();
        foreach (var level in new[] { 100, 120, 130, 160, 170 })
        {
            await scratch.ExecuteAsync($"ALTER DATABASE CURRENT SET COMPATIBILITY_LEVEL = {level};", token);
            await scratch.ExecuteAsync("CREATE OR ALTER PROCEDURE dbo.obs_split AS SELECT value FROM STRING_SPLIT(N'a,b', N',');", token);
            outcomes.Add($"{level}: {await Outcome(scratch, "SELECT value FROM STRING_SPLIT(N'a,b', N',');", token)} "
                + $"{await Outcome(scratch, "SELECT value, ordinal FROM STRING_SPLIT(N'a,b', N',', 1);", token)} {await Outcome(scratch, "EXEC dbo.obs_split;", token)}");
        }

        Assert.Equal(["100: 208 208 208", "120: 208 208 208", "130: ok ok ok", "160: ok ok ok", "170: ok ok ok"], outcomes);
    }
}
