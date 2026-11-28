namespace Haruspex.DifferentialTests.Server;

/// <summary>
/// The tests that set a variable of the process, which every test in it would see: xunit runs them when no other test is
/// running.
/// </summary>
[CollectionDefinition(DisableParallelization = true)]
public sealed class EnvironmentChanging;
