using System.Text.Json;

namespace Haruspex.DifferentialTests;

/// <summary>One call of a fixture's query file, through its generated function, against the fixture's seeded database.</summary>
/// <param name="Name">A unique name for the run, shown in messages, after the fixture's name.</param>
/// <param name="Fixture">The folder under <c>tests/fixtures</c>.</param>
/// <param name="Query">The query file's id: its path relative to the fixture's folder, such as <c>sql/getOrderLines.sql</c>.</param>
/// <param name="Parameters">
/// The arguments, by parameter name, as the JSON the function's parameters take: a query's types are inferred, not declared in
/// the database, so the file gives each value in the form its schema takes, not one the reader converts.
/// </param>
public sealed record QueryRun(string Name, string Fixture, string Query, IReadOnlyDictionary<string, JsonElement> Parameters)
{
    public override string ToString() => Name;
}
