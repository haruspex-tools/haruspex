namespace Haruspex.DifferentialTests.Server;

/// <summary>What the fixture does before any test needs the server, checked without one.</summary>
public class SqlServerFixtureTests
{
    /// <summary>
    /// Every test in the assembly shares the fixture, so a run of tests that need no server (the validator's, the pool's) must
    /// not wait for one: nothing is started, created or deployed until a test asks (<see cref="SqlServerFixture.RequireAsync"/>).
    /// </summary>
    [Fact]
    public async Task The_server_is_not_set_up_until_a_test_needs_it()
    {
        await using var fixture = new SqlServerFixture();

        await fixture.InitializeAsync();

        Assert.False(fixture.Started);
        Assert.Equal(0, fixture.Rooms);
    }
}
