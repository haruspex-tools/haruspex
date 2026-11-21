using System.Text.Json;

namespace Haruspex.Tests.Emit.Zod;

/// <summary>
/// What the pinned Zod accepts, observed with <c>tests/emit-check/src/zod-observations.mjs</c>: <c>z.json()</c>, which any JSON
/// is written as (docs/decisions/0094-any-json-is-a-type-of-its-own.md), and the ISO checks of dates and times against the forms
/// RFC 3339 allows, which decide how a column's declared <c>format</c> is read (the config layer's <c>DeclaredSchema</c>).
/// </summary>
public class ZodObservationTests
{
    private static JsonElement Observed()
    {
        EmitCheckPackage.RequireNode(RepositoryPaths.EmitCheck, "Zod was not observed");
        EmitCheckPackage.EnsureInstalled(RepositoryPaths.EmitCheck);
        Assert.True(EmitCheckPackage.TryRun("node", [Path.Combine("src", "zod-observations.mjs")], RepositoryPaths.EmitCheck, out var output, out var why), why);
        using var document = JsonDocument.Parse(output);
        return document.RootElement.Clone();
    }

    [Fact]
    public void Z_json_accepts_every_value_json_parse_gives_and_nothing_else()
    {
        var json = Observed().GetProperty("json");

        Assert.Equal(
            ["array", "boolean", "null", "number", "object", "string"],
            json.EnumerateObject().Where(p => p.Value.GetBoolean()).Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_string_s_length_is_counted_in_code_points_as_json_schema_counts_it_a_lone_surrogate_as_one()
    {
        var lengths = Observed().GetProperty("lengths");

        // Two characters outside the Basic Multilingual Plane are four UTF-16 code units and two code points; a lone surrogate
        // and a letter are two of each. At exactly n code points a maximum or minimum of n holds, and at n + 1 it doesn't.
        Assert.Equal((4, 2), (lengths.GetProperty("astral units").GetInt32(), lengths.GetProperty("lone units").GetInt32()));
        Assert.Equal(
            ["astral max 2", "astral min 2", "lone max 2", "lone min 2"],
            lengths.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.True).Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_iso_checks_refuse_forms_rfc_3339_allows_a_time_s_offset_a_lower_case_t_and_a_leap_second()
    {
        var iso = Observed().GetProperty("iso");

        Assert.Equal(
            ["date 2026-11-21", "date-time 2026-11-21T09:00:00Z", "time 09:00:00"],
            iso.EnumerateObject().Where(p => p.Value.GetBoolean()).Select(p => p.Name).Order(StringComparer.Ordinal));
    }
}
