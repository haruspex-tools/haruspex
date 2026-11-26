namespace Haruspex.DifferentialTests;

/// <summary>One call of a procedure, against a fixture's seeded database.</summary>
/// <param name="Name">A unique name for the run, shown in the test's name.</param>
/// <param name="Fixture">The folder under <c>tests/fixtures</c>.</param>
/// <param name="Procedure">The procedure's id.</param>
/// <param name="Parameters">The arguments, by parameter name, each with its declared type.</param>
/// <param name="Setup">
/// SQL run first, in a transaction rolled back afterwards: a way to see the tables in a state the seed does not
/// leave them in, such as empty, so that a procedure without parameters can still be run with no rows. A run with a
/// setup writes, so it runs alone in the database (a write session); every other run must only read, and fails if
/// its procedure writes (README.md, "Runs that write").
/// </param>
public sealed record ProcedureRun(
    string Name,
    string Fixture,
    string Procedure,
    IReadOnlyDictionary<string, RunArgument> Parameters,
    string? Setup = null)
{
    public override string ToString() => Name;
}
