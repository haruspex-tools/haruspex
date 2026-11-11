namespace Haruspex.Tests.Snapshotting;

/// <summary>The outcome of comparing a text with its verified snapshot.</summary>
internal sealed record SnapshotOutcome(bool Passed, string Message);
